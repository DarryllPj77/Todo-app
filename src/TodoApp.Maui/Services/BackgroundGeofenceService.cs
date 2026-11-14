using TodoApp.Domain.Location;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public interface IBackgroundGeofenceBlocker { bool IsBlocked { get; } long Generation { get; } long Block(); void Enable(long generation); }
public sealed record BackgroundGeofenceReport(string NativeId, NativeGeofenceTransition Transition, PositionFix? Position);
public interface IBackgroundGeofencePlatform
{
    bool IsSupported { get; }
    string Description { get; }
    Task RequestAccessAsync(CancellationToken token);
    Task RegisterAsync(BackgroundGeofenceRun run, CancellationToken token);
    Task CleanupAsync();
}
public interface IBackgroundGeofencePermissionSettings
{
    Task OpenPermissionSettingsAsync(CancellationToken token);
}
public sealed class UnsupportedBackgroundGeofencePlatform : IBackgroundGeofencePlatform
{
    public bool IsSupported => false;
    public string Description => "Background runs are unavailable on this build. Supported adapters require packaged Windows or Android with Google Play services. Manual reminders remain available.";
    public Task RequestAccessAsync(CancellationToken token) => throw new InvalidOperationException(Description);
    public Task RegisterAsync(BackgroundGeofenceRun run, CancellationToken token) => throw new InvalidOperationException(Description);
    public Task CleanupAsync() => Task.CompletedTask;
}

public sealed class BackgroundGeofenceService(SqliteBackgroundGeofenceStore store, LocationReminderService reminders,
    IBackgroundGeofencePlatform platform, IBackgroundGeofenceBlocker blocker, GeofenceProcessLease lease, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public bool IsSupported => platform.IsSupported;
    public string Capability => platform.Description;
    public bool CanOpenPermissionSettings => platform is IBackgroundGeofencePermissionSettings;
    public Task OpenPermissionSettingsAsync(CancellationToken token) => platform is IBackgroundGeofencePermissionSettings settings
        ? settings.OpenPermissionSettingsAsync(token) : throw new InvalidOperationException("This build does not provide mobile location settings.");
    public Task<BackgroundGeofenceRun?> ReadAsync() => store.ReadAsync();
    public async Task StartAsync(LocationReminder expected, CancellationToken token)
    {
        if (!platform.IsSupported) throw new InvalidOperationException(platform.Description);
        if (expected.Scope != "device") throw new ArgumentException("Background runs support device-local reminders only. Account places are not authorized for headless access.");
        token.ThrowIfCancellationRequested(); var generation = blocker.Block();
        using var ownership = await lease.AcquireAsync(token);
        token.ThrowIfCancellationRequested();
        if (blocker.Generation != generation) throw new OperationCanceledException("A newer Stop/start superseded this background opt-in.");
        BackgroundGeofenceRun? run = null;
        try
        {
            await platform.RequestAccessAsync(token); token.ThrowIfCancellationRequested();
            // The lease also excludes all page-active native sessions.
            await StopCoreAsync(blockControl: false); token.ThrowIfCancellationRequested();
            var snapshot = await reminders.ReadAsync("device", token); token.ThrowIfCancellationRequested();
            if (!snapshot.Reminders.Contains(expected) || !expected.Enabled) throw new LocationReminderConflictException();
            if (await reminders.NotificationPermissionAsync("device", token, request: false) != ReminderPermission.Allowed)
                throw new InvalidOperationException("Allow notifications before starting a background run.");
            var prior = await store.ReadAsync(); token.ThrowIfCancellationRequested();
            var now = time.GetUtcNow();
            run = await store.SaveAsync(prior, new(Guid.NewGuid(), checked((prior?.Revision ?? 0) + 1), expected, now, now.AddMinutes(30), false,
                BackgroundGeofenceState.Pending, "Registration pending; background callbacks remain blocked."));
            token.ThrowIfCancellationRequested(); await platform.RegisterAsync(run, token); token.ThrowIfCancellationRequested();
            var fix = await reminders.PositionAsync("device", token); token.ThrowIfCancellationRequested();
            var outside = fix.CapturedUtc >= run.StartedUtc && LocationRules.Classify(expected, fix, time.GetUtcNow()) == Proximity.Outside;
            if (outside) await reminders.ObserveNativeAsync("device", expected.Id, expected.ArmId, fix, token);
            token.ThrowIfCancellationRequested(); await RequireCurrentArmAsync(run, token);
            run = await store.SaveAsync(run, run with { Revision = run.Revision + 1, OutsideObserved = outside,
                State = BackgroundGeofenceState.Active, Status = "Background run active until the saved expiry, explicit Stop, invalidation or one arrival attempt." });
            token.ThrowIfCancellationRequested();
            if (time.GetUtcNow() >= run.ExpiresUtc) throw new InvalidOperationException("Background start expired. Explicitly retry.");
            blocker.Enable(generation); token.ThrowIfCancellationRequested();
        }
        catch
        {
            try { if (blocker.Generation == generation) blocker.Block(); } catch { }
            try { await StopCoreAsync(blockControl: false); } catch { /* Retain blocked journal/native metadata for explicit Stop retry. */ }
            throw;
        }
    }
    public async Task StopAsync(CancellationToken token = default)
    {
        // Durable opt-out precedes waits, decryption and native cleanup. A worker
        // checks this marker again immediately before notification delivery.
        try { blocker.Block(); } catch { /* Still attempt journal stop and native cleanup below. */ }
        using var ownership = await lease.AcquireAsync(token);
        await StopCoreAsync();
    }
    // Mobile boot/update/cold-Activity handling blocks synchronously first, then
    // cleans up only that generation. A delayed lifecycle callback cannot stop
    // a new run the user explicitly authorized in the meantime.
    public async Task StopForLifecycleAsync(long blockedGeneration, CancellationToken token)
    {
        using var ownership = await lease.AcquireAsync(token);
        if (blocker.Generation != blockedGeneration || !blocker.IsBlocked) return;
        await StopCoreAsync();
    }
    private async Task StopCoreAsync(bool blockControl = true)
    {
        Exception? problem = null;
        if (blockControl) try { if (!blocker.IsBlocked) blocker.Block(); } catch (Exception error) { problem = error; }
        try
        {
            var run = await store.ReadAsync();
            if (run is not null && run.State != BackgroundGeofenceState.Stopped)
                await store.SaveAsync(run, run with { Revision = run.Revision + 1, State = BackgroundGeofenceState.Stopped,
                    Status = "Stopped. Native cleanup may need retry; no automatic rearm or boot restoration." });
        }
        catch (Exception error) { problem = error; }
        await platform.CleanupAsync();
        if (problem is not null) throw new InvalidDataException("Background run is blocked and native cleanup was attempted. Protected journal is unreadable; nothing was reset.", problem);
    }
    private async Task RequireCurrentArmAsync(BackgroundGeofenceRun run, CancellationToken token)
    {
        var snapshot = await reminders.ReadAsync("device", token);
        var row = snapshot.Reminders.SingleOrDefault(r => r.Id == run.Reminder.Id);
        if (row is null || !row.Enabled || row.ArmId != run.Reminder.ArmId || row.TaskId != run.Reminder.TaskId || row.Recurrence != run.Reminder.Recurrence
            || row.Latitude != run.Reminder.Latitude || row.Longitude != run.Reminder.Longitude || row.RadiusMeters != run.Reminder.RadiusMeters)
            throw new LocationReminderConflictException();
    }
    public async Task ReconcileAsync(CancellationToken token)
    {
        var initial = await store.ReadAsync();
        if (initial is null || initial.State == BackgroundGeofenceState.Stopped) return;
        using var ownership = await lease.AcquireAsync(token);
        var current = await store.ReadAsync();
        if (current is null || current.State == BackgroundGeofenceState.Stopped) return;
        try
        {
            if (blocker.IsBlocked || current.State != BackgroundGeofenceState.Active || time.GetUtcNow() < current.StartedUtc || time.GetUtcNow() >= current.ExpiresUtc)
                throw new InvalidOperationException("Background run is blocked, incomplete or expired.");
            await RequireCurrentArmAsync(current, token);
            if (await reminders.NotificationPermissionAsync("device", token, request: false) != ReminderPermission.Allowed)
                throw new InvalidOperationException("Notification access changed.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Page departure does not revoke a completed background opt-in. */ }
        catch { await StopCoreAsync(); }
    }
    // Invoked only by the headless COM task. The callback drains reports AFTER
    // acquiring the cross-process lease, so busy activation cannot discard them.
    public async Task ProcessAsync(Func<IReadOnlyList<BackgroundGeofenceReport>> readReports, CancellationToken token)
    {
        using var ownership = await lease.AcquireAsync(token);
        try
        {
            if (blocker.IsBlocked) { await platform.CleanupAsync(); return; }
            var run = await store.ReadAsync(); token.ThrowIfCancellationRequested();
            if (run is null || run.State != BackgroundGeofenceState.Active || time.GetUtcNow() < run.StartedUtc || time.GetUtcNow() >= run.ExpiresUtc)
            { await StopCoreAsync(); return; }
            await RequireCurrentArmAsync(run, token);
            foreach (var report in readReports().OrderBy(r => r.Position?.CapturedUtc ?? DateTimeOffset.MinValue))
            {
                token.ThrowIfCancellationRequested();
                if (blocker.IsBlocked) { await StopCoreAsync(); return; }
                if (report.NativeId != run.EntryId && report.NativeId != run.ExitId) continue;
                if (report.Transition is NativeGeofenceTransition.Removed or NativeGeofenceTransition.Unavailable) { await StopCoreAsync(); return; }
                if (report.Position is not { } fix || fix.CapturedUtc < run.StartedUtc || fix.CapturedUtc >= run.ExpiresUtc) continue;
                if (time.GetUtcNow() >= run.ExpiresUtc) { await StopCoreAsync(); return; }
                await RequireCurrentArmAsync(run, token);
                var proximity = LocationRules.Classify(run.Reminder, fix, time.GetUtcNow());
                if (report.NativeId == run.ExitId && report.Transition == NativeGeofenceTransition.Exited && proximity == Proximity.Outside)
                {
                    await reminders.ObserveNativeAsync("device", run.Reminder.Id, run.Reminder.ArmId, fix, token);
                    token.ThrowIfCancellationRequested();
                    if (!run.OutsideObserved) run = await store.SaveAsync(run, run with { Revision = run.Revision + 1, OutsideObserved = true });
                }
                else if (report.NativeId == run.EntryId && report.Transition == NativeGeofenceTransition.Entered && proximity == Proximity.Inside && run.OutsideObserved)
                {
                    await reminders.ObserveNativeAsync("device", run.Reminder.Id, run.Reminder.ArmId, fix, token);
                    await StopCoreAsync(); return;
                }
            }
        }
        catch
        {
            try { blocker.Block(); } catch { }
            try { await StopCoreAsync(); } catch { }
            throw;
        }
    }
}
