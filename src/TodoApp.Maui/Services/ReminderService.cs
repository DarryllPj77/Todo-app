using TodoApp.Domain.Reminders;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed class ReminderService(SqliteReminderStore store, ReminderTaskCatalog catalog,
    IReminderPlatform platform, ClientSession session, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public string Status { get; private set; } = "Reminders are opt-in. No alert is scheduled from a due date alone.";
    public string DeliveryDescription => platform.DeliveryDescription;
    public event Action? Changed;

    public async Task<ReminderTask[]> TasksAsync()
    {
        var version = session.Version;
        var tasks = await catalog.ReadAsync(); session.EnsureVersion(version);
        return tasks.Where(t => !t.Completed).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<ReminderRecord[]> ReadAsync()
    {
        var version = session.Version;
        var rows = await store.ReadAsync(); session.EnsureVersion(version);
        return rows.Where(r => r.Scope == "device" || r.Scope == catalog.ActiveScope).ToArray();
    }
    public async Task SaveAsync(ReminderTask selected, DateTime day, TimeSpan at, TimeZoneInfo zone)
    {
        await gate.WaitAsync();
        try
        {
            var version = session.Version;
            var task = (await catalog.ReadAsync()).SingleOrDefault(t => t.Key == selected.Key);
            session.EnsureVersion(version);
            if (task is null || task.Completed || task.DueDate != selected.DueDate)
                throw new ArgumentException("The task changed. Refresh and review its reminder.");
            var instant = ReminderTime.Resolve(day, at, zone, time.GetUtcNow());
            var row = new ReminderRecord { Scope = task.Scope, TaskId = task.TaskId, Title = task.Title,
                TaskDueDate = task.DueDate, FireAtUtc = instant, TimeZoneId = zone.Id,
                WallTime = DateTime.SpecifyKind(day.Date.Add(at), DateTimeKind.Unspecified), Enabled = true, Status = "Saved; waiting for scheduling" };
            // Persist desired state first. A native failure must not lose the user's choice.
            await store.PutAsync(row);
        }
        finally { gate.Release(); }
        await RefreshSafelyAsync();
    }
    public async Task CancelAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            var row = (await store.ReadAsync()).SingleOrDefault(r => r.Key == key);
            if (row is null) return;
            if (row.Scope != "device" && row.Scope != catalog.ActiveScope) throw new ArgumentException("Select the active account's reminder.");
            row.Enabled = false; row.Status = "Canceled"; await store.PutAsync(row);
        }
        finally { gate.Release(); }
        await RefreshSafelyAsync();
    }
    public async Task RequestPermissionAsync()
    {
        string message;
        try
        {
            var result = await platform.PermissionAsync(true);
            message = result == ReminderPermission.Allowed ? "Permission is allowed. Check each reminder's scheduling status." :
                "Notifications are blocked or unavailable. Check system notification settings; task editing still works.";
        }
        catch (Exception) { message = "Notification permission is unavailable. Check system settings and platform setup."; }
        await RefreshSafelyAsync();
        Status = message + " " + Status;
        Publish();
    }

    // Callers can await cancellation/rescheduling without turning an already
    // committed task save into a failed save (which could duplicate a create).
    public async Task RefreshSafelyAsync()
    {
        await gate.WaitAsync();
        try { await ReconcileAsync(); }
        catch (Exception) { Status = "Couldn't reconcile notifications. Saved choices remain; retry Reminders before relying on delivery or cancellation."; }
        finally { gate.Release(); Publish(); }
    }
    private void Publish()
    {
        if (Changed is { } handlers)
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception) { /* UI observers cannot fail a committed task write. */ }
    }
    private async Task ReconcileAsync()
    {
        var version = session.Version;
        var tasks = (await catalog.ReadAsync()).ToDictionary(t => t.Key);
        session.EnsureVersion(version);
        var rows = await store.ReadAsync();
        if (rows.Length == 0) { Status = "No reminders saved. Choose a task and an explicit date/time."; return; }
        var now = time.GetUtcNow();
        var permission = await platform.PermissionAsync(false); // Never prompt during startup/retry.
        var slots = 0;
        var failures = 0;
        foreach (var row in rows.OrderBy(r => r.FireAtUtc).ThenBy(r => r.Id))
        {
            session.EnsureVersion(version);
            try
            {
                var active = row.Scope == "device" || row.Scope == catalog.ActiveScope;
                if (!active)
                {
                    await platform.CancelAsync(row.Id); row.Status = "Suspended: sign into this account to resume";
                }
                else if (!row.Enabled)
                    await platform.CancelAsync(row.Id);
                else if (!tasks.TryGetValue(row.Key, out var task) || task.Completed || task.DueDate != row.TaskDueDate)
                {
                    row.Enabled = false;
                    row.Status = "Canceled: task deleted/completed or due date changed; choose a new reminder";
                    await store.PutAsync(row); // Keep cancellation retryable if native removal fails.
                    await platform.CancelAsync(row.Id);
                }
                else if (permission != ReminderPermission.Allowed)
                {
                    await platform.CancelAsync(row.Id); row.Status = "Not scheduled: notification permission blocked/unavailable";
                }
                else if (row.FireAtUtc > now)
                {
                    row.Title = task.Title;
                    if (++slots > 32) { await platform.CancelAsync(row.Id); row.Status = "Waiting: only the nearest 32 reminders are scheduled"; }
                    else if (platform.SupportsScheduling)
                    {
                        await platform.ScheduleAsync(row.Id, row.FireAtUtc); row.Status = "Scheduled with OS; delivery is best effort";
                    }
                    else
                    {
                        await platform.CancelAsync(row.Id);
                        row.Status = row.Attempted ? "Already attempted; choose another time to rearm" : "Waiting: keep the Windows app running for this reminder";
                    }
                }
                else if (platform.SupportsScheduling)
                    row.Status = "Time passed; OS delivery is not confirmed";
                else if (!row.Attempted)
                {
                    row.Attempted = true;
                    row.Status = now - row.FireAtUtc <= TimeSpan.FromMinutes(5)
                        ? "Delivery attempted; check Notification Center" : "Missed while app was closed/asleep; choose another time";
                    await store.PutAsync(row); // At most one attempt, even if showing/crashing is uncertain.
                    if (now - row.FireAtUtc <= TimeSpan.FromMinutes(5)) await platform.ShowAsync(row.Id);
                }
                if (session.Version != version) { await platform.CancelAsync(row.Id); session.EnsureVersion(version); }
                await store.PutAsync(row);
            }
            catch (Exception) when (session.Version == version)
            {
                failures++;
                row.Status = "Scheduling/cancellation failed or unconfirmed; retry before relying on it";
                await store.PutAsync(row);
            }
        }
        Status = (failures > 0 ? $"{failures} reminder operations failed/unconfirmed. " : "") + "Reminders checked. Inspect each row for blocked, canceled, waiting, or scheduled status. " + platform.DeliveryDescription;
    }
}
