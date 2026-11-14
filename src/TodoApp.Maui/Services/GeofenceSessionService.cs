using TodoApp.Domain.Location;

namespace TodoApp.Maui.Services;

// One opt-in owner across app windows. Sessions are deliberately ephemeral:
// no startup restoration, OS background activation or automatic rearm.
public sealed class GeofenceSessionService : IDisposable
{
    private readonly LocationReminderService reminders;
    private readonly INativeGeofencePlatform platform;
    private readonly ClientSession session;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private Guid? owner;
    private Guid run;
    private string scope = "";
    private int version;
    private bool active;
    private DateTimeOffset started;
    private IReadOnlyList<LocationReminder> registered = Array.Empty<LocationReminder>();
    private readonly HashSet<Guid> outside = new();
    public event Action? Changed;
    public string Notice { get; private set; } = "Native session stopped. Start requires explicit consent; no closed-app monitoring.";
    public string Capability => platform.Description;
    public bool IsSupported => platform.IsSupported;
    public bool IsActiveFor(Guid candidate) => active && owner == candidate;
    public GeofenceSessionService(LocationReminderService reminders, INativeGeofencePlatform platform, ClientSession session, TimeProvider? clock = null)
    {
        this.reminders = reminders; this.platform = platform; this.session = session; this.clock = clock ?? TimeProvider.System;
        session.Changed += SessionChanged;
    }
    public async Task StartAsync(Guid candidate, string requestedScope, CancellationToken token)
    {
        if (candidate == Guid.Empty) throw new ArgumentException("A native session needs a page owner.");
        var capturedVersion = session.Version; token.ThrowIfCancellationRequested();
        await gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested(); session.EnsureVersion(capturedVersion);
            if (active && owner != candidate) throw new InvalidOperationException("Another window owns the native location session. Stop it there first.");
            if (!platform.IsSupported) throw new InvalidOperationException(platform.Description);
            await StopCoreAsync(); token.ThrowIfCancellationRequested(); session.EnsureVersion(capturedVersion);
            owner = candidate; scope = requestedScope; version = capturedVersion; run = Guid.NewGuid();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var cancellation = lifetime.Token; var capturedRun = run; Ensure(capturedRun);
            var snapshot = await reminders.ReadAsync(scope, cancellation); Ensure(capturedRun);
            registered = snapshot.Reminders.Where(r => r.Enabled).ToArray();
            if (registered.Count is < 1 or > 10) throw new InvalidOperationException("Arm 1-10 places in this workspace before starting a native session. Disable extra places first.");
            if (await reminders.NotificationPermissionAsync(scope, cancellation, request: false) != ReminderPermission.Allowed)
                throw new InvalidOperationException("Allow notifications before starting a native session.");
            Ensure(capturedRun); started = this.clock.GetUtcNow();
            var specs = registered.Select(r => new NativeGeofenceSpec(r.Id, r.ArmId, r.Latitude, r.Longitude, r.RadiusMeters)).ToArray();
            await platform.StartAsync(specs, report => ReceiveAsync(capturedRun, report), cancellation); Ensure(capturedRun);
            // Explicit Start also authorizes one foreground bootstrap sample.
            // Only outside can seed a new session: starting inside never alerts.
            var fix = await reminders.PositionAsync(scope, cancellation); Ensure(capturedRun);
            foreach (var row in registered)
                if (fix.CapturedUtc >= started && LocationRules.Classify(row, fix, this.clock.GetUtcNow()) == Proximity.Outside)
                {
                    await reminders.ObserveNativeAsync(scope, row.Id, row.ArmId, fix, cancellation); Ensure(capturedRun); outside.Add(row.Id);
                }
            await ValidateRowsAsync(capturedRun); Ensure(capturedRun); active = true;
            SetNotice("Native session active for up to 30 minutes on this page. Outside then conservative inner entry is required. The first arrival attempt stops the session; other arms remain saved.");
        }
        catch
        {
            if (owner == candidate)
            {
                try { await StopCoreAsync(); SetNotice("Native session stopped after an incomplete start. Review permissions and saved places, then explicitly retry."); }
                catch { SetNotice("Native cleanup failed. Session callbacks are blocked; retry Stop. OS regions expire within 30 minutes of registration."); }
            }
            throw;
        }
        finally { gate.Release(); }
    }
    private void Ensure(Guid expectedRun)
    {
        if (run != expectedRun || lifetime is null) throw new OperationCanceledException();
        lifetime.Token.ThrowIfCancellationRequested(); session.EnsureVersion(version);
        if (clock.GetUtcNow() >= started.AddMinutes(30) && started != default) throw new InvalidOperationException("Native session expired. Explicitly start another session.");
    }
    private async Task ValidateRowsAsync(Guid expectedRun)
    {
        var snapshot = await reminders.ReadAsync(scope, lifetime!.Token); Ensure(expectedRun);
        foreach (var row in registered)
            if (!snapshot.Reminders.Any(r => r.Id == row.Id && r.ArmId == row.ArmId && r.Enabled
                && r.TaskId == row.TaskId && r.Recurrence == row.Recurrence && r.Latitude == row.Latitude && r.Longitude == row.Longitude && r.RadiusMeters == row.RadiusMeters))
                throw new InvalidOperationException("A registered task/place changed. Native session stopped; refresh and explicitly start again.");
    }
    private async Task ReceiveAsync(Guid expectedRun, NativeGeofenceReport report)
    {
        await gate.WaitAsync();
        try
        {
            if (!active || run != expectedRun) return;
            Ensure(expectedRun);
            if (report.Transition == NativeGeofenceTransition.Unavailable) throw new InvalidOperationException("Native location monitoring became unavailable. Check OS settings and explicitly restart.");
            var row = registered.SingleOrDefault(r => r.Id == report.ReminderId && r.ArmId == report.ArmId);
            if (row is null) return;
            if (report.Transition == NativeGeofenceTransition.Removed) throw new InvalidOperationException("The OS removed a native region. Refresh and explicitly restart; no automatic registration occurs.");
            if (report.Position is not { } fix || fix.CapturedUtc < started) return;
            await ValidateRowsAsync(expectedRun); Ensure(expectedRun);
            var proximity = LocationRules.Classify(row, fix, clock.GetUtcNow());
            if (report.Transition == NativeGeofenceTransition.Exited && proximity == Proximity.Outside)
            {
                await reminders.ObserveNativeAsync(scope, row.Id, row.ArmId, fix, lifetime!.Token); Ensure(expectedRun); outside.Add(row.Id);
            }
            else if (report.Transition == NativeGeofenceTransition.Entered && proximity == Proximity.Inside && outside.Contains(row.Id))
            {
                await reminders.ObserveNativeAsync(scope, row.Id, row.ArmId, fix, lifetime!.Token); Ensure(expectedRun);
                await StopCoreAsync(); SetNotice("Arrival check finished; its attempt may be consumed even if delivery failed/was blocked. Native session stopped. Refresh saved status before explicitly restarting.");
            }
        }
        catch (Exception)
        {
            try { await StopCoreAsync(); SetNotice("Native session stopped after a location, task, permission or delivery change. Refresh saved status and explicitly restart if needed."); }
            catch { SetNotice("Native cleanup failed. Callbacks are blocked; retry Stop. OS regions expire within 30 minutes of registration."); }
        }
        finally { gate.Release(); }
    }
    public async Task ReconcileAsync(Guid candidate)
    {
        await gate.WaitAsync();
        try
        {
            if (!active || owner != candidate) return;
            var capturedRun = run; Ensure(capturedRun); await ValidateRowsAsync(capturedRun);
            if (await reminders.NotificationPermissionAsync(scope, lifetime!.Token, request: false) != ReminderPermission.Allowed)
                throw new InvalidOperationException("Notification permission changed.");
            Ensure(capturedRun);
        }
        catch
        {
            try { await StopCoreAsync(); SetNotice("Native session stopped after expiry, permission, task or workspace changes. Refresh before explicitly starting again."); }
            catch { SetNotice("Native cleanup failed. Retry Stop; callbacks are blocked and OS regions have a 30-minute expiry."); }
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync(Guid candidate)
    {
        Invalidate(candidate); await gate.WaitAsync();
        try
        {
            if (owner is not null && owner != candidate) return;
            await StopCoreAsync(); SetNotice("Native session stopped. Saved reminder arms remain; starting again requires consent and new outside evidence.");
        }
        finally { gate.Release(); }
    }
    private void Invalidate(Guid? candidate)
    {
        if (candidate is null || owner == candidate) { active = false; try { lifetime?.Cancel(); } catch (ObjectDisposedException) { } }
    }
    public void Cancel(Guid candidate) { Invalidate(candidate); _ = StopSafelyAsync(candidate); }
    private async Task StopSafelyAsync(Guid candidate)
    {
        try { await StopAsync(candidate); }
        catch { SetNotice("Native cleanup failed. Retry Stop; callbacks are blocked and OS regions expire within 30 minutes."); }
    }
    private async Task StopCoreAsync()
    {
        active = false; lifetime?.Cancel();
        await platform.StopAsync();
        lifetime?.Dispose(); lifetime = null; owner = null; run = Guid.Empty; registered = Array.Empty<LocationReminder>(); outside.Clear(); started = default;
    }
    private void SetNotice(string value) { Notice = value; Changed?.Invoke(); }
    private void SessionChanged() { var prior = owner; Invalidate(null); if (prior is { } candidate) _ = StopSafelyAsync(candidate); }
    public void Dispose() { session.Changed -= SessionChanged; Invalidate(null); }
}
