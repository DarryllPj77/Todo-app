using TodoApp.Domain.Location;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public interface IForegroundLocationProvider
{
    Task<PositionFix> ReadAsync(CancellationToken cancellationToken);
}
public sealed record LocationSnapshot(IReadOnlyList<LocationTask> Tasks, IReadOnlyList<LocationReminder> Reminders);
public sealed class LocationReminderService(SqliteLocationReminderStore store, ILocationTaskSource tasks,
    IForegroundLocationProvider location, IReminderPlatform notifications, ClientSession session, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    // Capture authorization BEFORE waiting: queued work cannot move to a new account.
    private async Task<T> RunAsync<T>(string scope, CancellationToken token, Func<Action, Task<T>> action)
    {
        var version = session.Version;
        void Ensure()
        {
            token.ThrowIfCancellationRequested(); session.EnsureVersion(version);
            if (!tasks.CanAccess(scope)) throw new ApiClientException("This location workspace is no longer available. Sign in again.");
        }
        Ensure(); await gate.WaitAsync(token);
        try { Ensure(); return await action(Ensure); }
        finally { gate.Release(); }
    }
    public Task<LocationSnapshot> ReadAsync(string scope, CancellationToken token) => RunAsync(scope, token, async ensure =>
    {
        var current = await tasks.ReadAsync(scope); ensure();
        var rows = await store.ReadAsync(scope); ensure();
        var valid = new List<LocationReminder>();
        foreach (var row in rows)
        {
            var saved = row;
            if (row.Enabled && !Matches(row, current))
            {
                saved = await store.ChangeAsync(row, ArrivalState.TaskChanged, "Task completed, removed, conflicted or repeat occurrence changed. Review and explicitly rearm."); ensure();
                await notifications.CancelAsync(saved.NotificationId); ensure();
            }
            valid.Add(saved);
        }
        return new LocationSnapshot(current, valid);
    });
    public Task<LocationReminder> ArmAsync(string scope, LocationReminder? expected, LocationReminderDraft draft, CancellationToken token) => RunAsync(scope, token, async ensure =>
    {
        if (expected is not null && expected.Scope != scope) throw new LocationReminderConflictException();
        var current = await tasks.ReadAsync(scope); ensure();
        var task = current.SingleOrDefault(t => t.Id == draft.TaskId) ?? throw new ArgumentException("Refresh and choose a pending task without a sync conflict.");
        // Remove a previous generic notification before explicitly starting a new arm.
        if (expected is not null) { await notifications.CancelAsync(expected.NotificationId); ensure(); }
        var result = await store.ArmAsync(scope, expected?.Id ?? Guid.NewGuid(), draft, task, expected?.Revision); ensure(); return result;
    });
    public Task<bool> RemoveAsync(string scope, LocationReminder expected, bool delete, CancellationToken token) => RunAsync(scope, token, async ensure =>
    {
        if (expected.Scope != scope) throw new LocationReminderConflictException();
        var disabled = await store.ChangeAsync(expected, ArrivalState.Disabled, "Disabled. No location checks or alerts for this reminder."); ensure();
        // Keep disabled metadata if native cancellation fails so a retry is possible.
        await notifications.CancelAsync(disabled.NotificationId); ensure();
        if (delete) { await store.DeleteAsync(disabled); ensure(); }
        return true;
    });
    public Task<ReminderPermission> NotificationPermissionAsync(string scope, CancellationToken token, bool request = true) => RunAsync(scope, token, async ensure =>
    {
        var result = await notifications.PermissionAsync(request); ensure(); return result;
    });
    public Task<PositionFix> PositionAsync(string scope, CancellationToken token) => RunAsync(scope, token, async ensure =>
    {
        var fix = await location.ReadAsync(token); ensure();
        LocationRules.Coordinates(fix.Latitude, fix.Longitude);
        if (fix.Mocked || fix.CapturedUtc > time.GetUtcNow().AddSeconds(10) || time.GetUtcNow() - fix.CapturedUtc > TimeSpan.FromMinutes(2)
            || fix.AccuracyMeters is not { } accuracy || !double.IsFinite(accuracy) || accuracy <= 0 || accuracy > 500)
            throw new InvalidOperationException("Location was stale, simulated or insufficiently accurate. Enter coordinates or try again outdoors.");
        return fix;
    });
    public Task<string> CheckAsync(string scope, CancellationToken token) => RunAsync(scope, token,
        ensure => CheckCoreAsync(scope, token, ensure, () => location.ReadAsync(token)));
    // Native reports pass through the same persistence, quality, task and delivery rules.
    // Restrict each report to the exact registered arm; it cannot affect other places.
    public Task<string> ObserveNativeAsync(string scope, Guid id, Guid arm, PositionFix fix, CancellationToken token) => RunAsync(scope, token,
        ensure => CheckCoreAsync(scope, token, ensure, () => Task.FromResult(fix), id, arm));
    private async Task<string> CheckCoreAsync(string scope, CancellationToken token, Action ensure,
        Func<Task<PositionFix>> readFix, Guid? id = null, Guid? arm = null)
    {
        var current = await tasks.ReadAsync(scope); ensure();
        var rows = await store.ReadAsync(scope); ensure();
        var active = new List<LocationReminder>();
        foreach (var row in rows.Where(r => r.Enabled && (id is null || r.Id == id && r.ArmId == arm)))
        {
            if (Matches(row, current)) active.Add(row);
            else
            {
                await store.ChangeAsync(row, ArrivalState.TaskChanged, "Task changed. Review and explicitly rearm."); ensure();
                await notifications.CancelAsync(row.NotificationId); ensure();
            }
        }
        if (active.Count == 0) return "No eligible armed reminders. Refresh and review task changes.";
        var permission = await notifications.PermissionAsync(false); ensure();
        if (permission != ReminderPermission.Allowed) return "Notifications are denied/unavailable. Use Allow/check notifications first; no location was requested.";
        var fix = await readFix(); ensure();
        var attempted = 0; var outside = 0;
        foreach (var row in active)
        {
            ensure();
            // Re-read task and saved row after the potentially slow OS location request.
            current = await tasks.ReadAsync(scope); ensure();
            var latest = (await store.ReadAsync(scope)).SingleOrDefault(r => r.Id == row.Id); ensure();
            if (latest != row) continue;
            if (!Matches(row, current))
            {
                await store.ChangeAsync(row, ArrivalState.TaskChanged, "Task changed during the check. Review and explicitly rearm."); ensure();
                await notifications.CancelAsync(row.NotificationId); ensure(); continue;
            }
            var proximity = LocationRules.Classify(row, fix, time.GetUtcNow());
            if (proximity == Proximity.Outside && row.State == ArrivalState.Armed)
            {
                await store.ChangeAsync(row, ArrivalState.OutsideObserved, "Outside confirmed. A later reliable inside check can attempt one generic notification."); ensure(); outside++;
            }
            else if (proximity == Proximity.Inside && row.State == ArrivalState.OutsideObserved)
            {
                // Persist consumption BEFORE native delivery: at most one attempt per arm.
                var consumed = await store.ChangeAsync(row, ArrivalState.Attempted, "Arrival notification attempt consumed. Delivery is not guaranteed; explicitly rearm for another arrival.");
                try
                {
                    ensure();
                    if ((await store.ReadAsync(scope)).SingleOrDefault(r => r.Id == row.Id) != consumed) continue;
                    ensure(); await notifications.ShowAsync(row.NotificationId); attempted++; ensure();
                    if ((await store.ReadAsync(scope)).SingleOrDefault(r => r.Id == row.Id) != consumed) await notifications.CancelAsync(row.NotificationId);
                    ensure();
                }
                catch { await notifications.CancelAsync(row.NotificationId); throw; }
            }
        }
        return $"Check finished: {outside} outside transitions; {attempted} arrival notification attempts. Initial inside, uncertain or inaccurate samples do not trigger an alert. No background monitoring.";
    }
    private static bool Matches(LocationReminder row, IReadOnlyList<LocationTask> current) => current.Any(t => t.Id == row.TaskId && t.Recurrence == row.Recurrence);
}
