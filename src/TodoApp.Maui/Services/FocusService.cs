using TodoApp.Domain.Focus;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

// One shared coordinator for all app windows. The OS file lease excludes a
// second app process; revision checks protect persisted checkpoints as well.
public sealed class FocusService : IDisposable
{
    private readonly SqliteFocusStore store;
    private readonly IFocusTaskSource tasks;
    private readonly ClientSession session;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IDisposable? lease;
    private FocusEntry? saved;
    private FocusSession? current;
    private long lastTimestamp, checkpointTimestamp;
    private bool initialized, faulted, disposed, suspended;
    public string Notice { get; private set; } = "";
    public FocusService(SqliteFocusStore store, IFocusTaskSource tasks, ClientSession session, TimeProvider? clock = null)
    {
        this.store = store; this.tasks = tasks; this.session = session; time = clock ?? TimeProvider.System;
        session.Changed += AccountChanged;
    }
    private async Task<bool> InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lease is null) lease = store.TryAcquireRuntimeLease();
        if (lease is null) return false;
        if (!initialized || faulted)
        {
            var abandoned = await store.ActiveAsync();
            if (abandoned is not null)
                await store.SaveAsync(abandoned, FocusRules.Finish(abandoned.Session,
                    faulted ? FocusEndReason.StorageFailure : FocusEndReason.AppRestarted, abandoned.Session.UpdatedAtUtc));
            if (abandoned is not null) Notice = faulted
                ? "Tracking was interrupted. Only the last saved checkpoint was retained. Start a new session after reviewing history."
                : "A previous session was interrupted. Its last saved checkpoint was retained; closed-app time was not credited.";
            current = null; saved = null; faulted = false; initialized = true;
        }
        return true;
    }
    private void EnsureScope(string scope)
    { if (!tasks.CanAccess(scope)) throw new ApiClientException("Sign in again to select this account's focus workspace."); }
    public async Task InitializeSafelyAsync()
    {
        await gate.WaitAsync();
        try { _ = await InitializeAsync(); }
        catch (Exception) { Notice = "Focus history could not be opened. Saved data was not reset; open Focus & history to retry."; }
        finally { gate.Release(); }
    }
    public async Task ResumeAppSafelyAsync()
    {
        await gate.WaitAsync();
        try { suspended = false; _ = await InitializeAsync(); }
        catch (Exception) { Notice = "Focus history could not be opened. Saved data was not reset; open Focus & history to retry."; }
        finally { gate.Release(); }
    }
    public async Task<FocusSnapshot> ReadAsync(string scope)
    {
        EnsureScope(scope); await gate.WaitAsync();
        try
        {
            var owns = await InitializeAsync(); EnsureScope(scope);
            if (owns && current is not null && !tasks.CanAccess(current.Scope)) await EndAsync(FocusEndReason.AccountChanged, false);
            var active = owns ? current is null ? null : new FocusEntry(saved!.Sequence, saved.Revision, current) : await store.ActiveAsync();
            var history = (await store.ReadAsync(scope)).ToList(); var preferences = await store.PreferencesAsync(scope); EnsureScope(scope);
            if (active?.Session.Scope == scope)
            {
                var index = history.FindIndex(e => e.Session.Id == active.Session.Id);
                if (index >= 0) history[index] = active;
            }
            return new(preferences, history.AsReadOnly(), active?.Session.Scope == scope ? active : null,
                active is not null && active.Session.Scope != scope, owns);
        }
        finally { gate.Release(); }
    }
    public async Task StartAsync(string scope, FocusKind kind, Guid? taskId, FocusPreferences preferences)
    {
        EnsureScope(scope); await gate.WaitAsync();
        try
        {
            if (!await InitializeAsync()) throw new InvalidOperationException("Another app instance controls focus tracking. Close it before starting here.");
            EnsureScope(scope);
            if (suspended) throw new InvalidOperationException("Resume the app before starting a focus session.");
            if (current is not null) throw new InvalidOperationException("A session is running or paused. Return to its workspace and finish it first.");
            FocusTask? task = null;
            if (kind == FocusKind.Focus) task = (await tasks.ReadAsync(scope)).SingleOrDefault(t => t.Id == taskId)
                ?? throw new ArgumentException("The task is no longer pending in this workspace. Refresh and choose a task.");
            EnsureScope(scope);
            var started = FocusRules.Start(scope, task, kind, preferences, time.GetUtcNow());
            saved = await store.StartAsync(started, preferences); current = saved.Session;
            lastTimestamp = checkpointTimestamp = time.GetTimestamp(); Notice = "";
            if (!tasks.CanAccess(scope)) { await EndAsync(FocusEndReason.AccountChanged, false); throw new ApiClientException("Account changed; the session was interrupted."); }
        }
        finally { gate.Release(); }
    }
    // Runtime calls this each second; failures stop crediting and are recovered
    // from the durable checkpoint when the user next opens/refreshes the page.
    public async Task TickSafelyAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed || suspended || !initialized || faulted || current is null) return;
            await AdvanceAsync(false);
        }
        catch (Exception)
        {
            faulted = true; current = null; saved = null;
            Notice = "Focus tracking stopped because task or focus storage was unavailable. Refresh to review the last saved checkpoint.";
        }
        finally { gate.Release(); }
    }
    private async Task AdvanceAsync(bool forceCheckpoint)
    {
        if (current is null) return;
        if (!tasks.CanAccess(current.Scope)) { await EndAsync(FocusEndReason.AccountChanged, false); return; }
        if (current.Status == FocusStatus.Paused) return;
        var stamp = time.GetTimestamp(); var delta = time.GetElapsedTime(lastTimestamp, stamp);
        var checkpoint = forceCheckpoint || time.GetElapsedTime(checkpointTimestamp, stamp) >= TimeSpan.FromSeconds(5);
        if (checkpoint && current.Kind == FocusKind.Focus)
        {
            var task = (await tasks.ReadAsync(current.Scope)).SingleOrDefault(t => t.Id == current.TaskId);
            if (!tasks.CanAccess(current.Scope)) { await EndAsync(FocusEndReason.AccountChanged, false); return; }
            if (task is null) { await EndTaskChangedAsync(FocusEndReason.TaskUnavailable); return; }
            if (task.Recurrence != current.TaskRecurrence) { await EndTaskChangedAsync(FocusEndReason.OccurrenceChanged); return; }
        }
        current = FocusRules.Advance(current, delta, time.GetUtcNow()); lastTimestamp = stamp;
        if (checkpoint || !current.IsActive) await PersistAsync();
    }
    private async Task PersistAsync()
    {
        if (current is null || saved is null) return;
        saved = await store.SaveAsync(saved, current); checkpointTimestamp = time.GetTimestamp();
        if (!current.IsActive) { current = null; saved = null; }
    }
    private Task EndTaskChangedAsync(FocusEndReason reason)
    {
        // The task was not checked on each display tick. Discard the unchecked
        // tail rather than attributing it to a deleted/completed/new occurrence.
        if (current is not null && saved is not null) current = current with { ElapsedTicks = saved.Session.ElapsedTicks };
        return EndAsync(reason, false);
    }
    private async Task EndAsync(FocusEndReason reason, bool creditRecent)
    {
        if (current is null) return;
        if (creditRecent) await AdvanceAsync(true);
        if (current is null) return;
        current = FocusRules.Finish(current, reason, time.GetUtcNow()); await PersistAsync();
    }
    public Task PauseAsync(string scope, Guid id) => ControlAsync(scope, id, async () =>
    {
        await AdvanceAsync(true);
        if (current is null) return;
        current = FocusRules.Pause(current, time.GetUtcNow()); await PersistAsync();
    });
    public Task ResumeAsync(string scope, Guid id) => ControlAsync(scope, id, async () =>
    {
        if (current!.Status != FocusStatus.Paused) return;
        if (current.Kind == FocusKind.Focus)
        {
            var task = (await tasks.ReadAsync(scope)).SingleOrDefault(t => t.Id == current.TaskId);
            if (task is null) { await EndAsync(FocusEndReason.TaskUnavailable, false); return; }
            if (task.Recurrence != current.TaskRecurrence) { await EndAsync(FocusEndReason.OccurrenceChanged, false); return; }
        }
        EnsureScope(scope); current = FocusRules.Resume(current, time.GetUtcNow()); lastTimestamp = checkpointTimestamp = time.GetTimestamp(); await PersistAsync();
    });
    public Task StopAsync(string scope, Guid id) => ControlAsync(scope, id, () => EndAsync(FocusEndReason.UserStopped, true));
    private async Task ControlAsync(string scope, Guid id, Func<Task> action)
    {
        EnsureScope(scope); await gate.WaitAsync();
        try
        {
            if (!await InitializeAsync()) throw new InvalidOperationException("Another app instance controls focus tracking.");
            EnsureScope(scope);
            if (current is null) return; // Repeated stop/completion never creates a ledger entry.
            if (current.Id != id || current.Scope != scope) throw new FocusConflictException();
            await action();
        }
        catch
        {
            // A failed checkpoint must not continue accruing unpersisted time.
            if (current is not null && saved is not null && current != saved.Session) { faulted = true; current = null; saved = null; }
            throw;
        }
        finally { gate.Release(); }
    }
    public async Task SuspendAsync()
    {
        await gate.WaitAsync();
        try { suspended = true; if (!disposed && initialized && !faulted) await EndAsync(FocusEndReason.AppSuspended, true); }
        catch (Exception) { faulted = true; current = null; saved = null; Notice = "Suspension interrupted tracking; the last saved checkpoint will be recovered."; }
        finally { gate.Release(); }
    }
    private async void AccountChanged()
    {
        await gate.WaitAsync();
        try { if (!disposed && current is not null && !tasks.CanAccess(current.Scope)) await EndAsync(FocusEndReason.AccountChanged, false); }
        catch (Exception) { faulted = true; current = null; saved = null; }
        finally { gate.Release(); }
    }
    public void Dispose()
    { disposed = true; session.Changed -= AccountChanged; lease?.Dispose(); lease = null; }
}
