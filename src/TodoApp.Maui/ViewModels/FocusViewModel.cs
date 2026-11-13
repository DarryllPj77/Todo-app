using System.Collections.ObjectModel;
using System.Globalization;
using TodoApp.Domain.Focus;
using TodoApp.Domain.Recurrence;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed record FocusTaskRow(FocusTask Task, string TimeSpent)
{
    public Guid Id => Task.Id;
    public string Title => Task.Title + " (" + Task.Id.ToString("N")[..8] + ")";
}
public sealed record FocusHistoryRow(FocusEntry Entry)
{
    public string Title => Entry.Session.Title;
    public string Summary => $"{Entry.Session.Kind} | {Entry.Session.Status} | {FocusViewModel.Duration(Entry.Session.ElapsedTicks)} | {Entry.Session.EndReason?.ToString() ?? "In progress"}";
    public string Detail => Entry.Session.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) +
        (Entry.Session.TaskId is { } id ? " | Task " + id.ToString("N")[..8] : " | Break: excluded from TimeSpent") +
        (Entry.Session.TaskRecurrence?.IsActive == true ? $" | Occurrence {Entry.Session.TaskRecurrence.Occurrence + 1}" : "");
}
public sealed record FocusTaskTotal(string Title, string TimeSpent);

public sealed class FocusViewModel : ObservableViewModel, IDisposable
{
    private readonly FocusService focus;
    private readonly IFocusTaskSource source;
    private readonly ClientSession session;
    private int generation, operation, observedVersion;
    private bool busy, polling, canControl, otherScopeActive;
    private HierarchyWorkspace workspace;
    private FocusTaskRow? selected;
    private FocusEntry? active;
    private string focusMinutes = "25", shortMinutes = "5", longMinutes = "15", error = "", status = "Refresh to load focus history.", totals = "", suggestion = "";
    public ObservableCollection<FocusTaskRow> Tasks { get; } = new();
    public ObservableCollection<FocusHistoryRow> History { get; } = new();
    public ObservableCollection<FocusTaskTotal> TaskTotals { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == HierarchyWorkspace.Device || session.Current is not null);
    public bool HasActive => active is not null;
    public FocusEntry? Active { get => active; private set { SetProperty(ref active, value); OnPropertyChanged(nameof(Timer)); OnPropertyChanged(nameof(ActiveTitle)); Notify(); } }
    public string Timer => Active is { } entry ? Duration((long)Math.Ceiling(entry.Session.Remaining.TotalSeconds) * TimeSpan.TicksPerSecond) : "00:00:00";
    public string ActiveTitle => Active is { } entry ? entry.Session.Title + " - " + entry.Session.Kind + " / " + entry.Session.Status : "No active session in this workspace";
    public FocusTaskRow? SelectedTask { get => selected; set { SetProperty(ref selected, value); Notify(); } }
    public string FocusMinutes { get => focusMinutes; set => SetProperty(ref focusMinutes, value ?? ""); }
    public string ShortBreakMinutes { get => shortMinutes; set => SetProperty(ref shortMinutes, value ?? ""); }
    public string LongBreakMinutes { get => longMinutes; set => SetProperty(ref longMinutes, value ?? ""); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Totals { get => totals; private set => SetProperty(ref totals, value); }
    public string Suggestion { get => suggestion; private set => SetProperty(ref suggestion, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand StartFocusCommand { get; }
    public AsyncCommand StartShortBreakCommand { get; }
    public AsyncCommand StartLongBreakCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand StopCommand { get; }
    public FocusViewModel(FocusService focus, IFocusTaskSource source, ClientSession session)
    {
        this.focus = focus; this.source = source; this.session = session; observedVersion = session.Version;
        RefreshCommand = Command(RefreshAsync);
        StartFocusCommand = Command(() => StartAsync(FocusKind.Focus), () => CanStart && SelectedTask is not null);
        StartShortBreakCommand = Command(() => StartAsync(FocusKind.ShortBreak), () => CanStart);
        StartLongBreakCommand = Command(() => StartAsync(FocusKind.LongBreak), () => CanStart);
        PauseCommand = Command(() => ControlAsync("pause"), () => canControl && Active?.Session.Status == FocusStatus.Running);
        ResumeCommand = Command(() => ControlAsync("resume"), () => canControl && Active?.Session.Status == FocusStatus.Paused);
        StopCommand = Command(() => ControlAsync("stop"), () => canControl && Active is not null);
        session.Changed += SessionChanged;
    }
    private bool CanStart => canControl && Active is null && !otherScopeActive;
    private sealed record Context(string Scope, HierarchyWorkspace Workspace, int Generation, int Version);
    private Context Capture()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a focus workspace.");
        var scope = Workspace == HierarchyWorkspace.Device ? "device" : session.Current is { } saved
            ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in to select this account's local focus history.");
        if (!source.CanAccess(scope)) throw new ApiClientException("The account session expired. Sign in again.");
        return new(scope, Workspace, generation, session.Version);
    }
    private void Ensure(Context context)
    {
        if (context.Generation != generation || context.Workspace != Workspace) throw new ApiClientException("Workspace changed; old focus results were discarded.");
        if (Workspace == HierarchyWorkspace.SyncedAccount) session.EnsureVersion(context.Version);
        if (!source.CanAccess(context.Scope)) throw new ApiClientException("The account session expired. Sign in again.");
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; operation++; Error = ""; Notify();
        try { await action(); }
        catch (ApiClientException exception) { if (Workspace == HierarchyWorkspace.SyncedAccount) Clear(); Error = exception.Message; }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (InvalidOperationException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't read/save focus history. Saved data was not reset. Refresh to recover the last checkpoint."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the focus action.");
    private async Task RefreshAsync()
    {
        var context = Capture(); var tasks = await source.ReadAsync(context.Scope); Ensure(context);
        var snapshot = await focus.ReadAsync(context.Scope); Ensure(context);
        var selectedId = SelectedTask?.Id; Tasks.Clear();
        foreach (var task in tasks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id)) Tasks.Add(new(task, Duration(FocusRules.TimeSpentTicks(snapshot.History, task.Id))));
        SelectedTask = Tasks.FirstOrDefault(t => t.Id == selectedId);
        FocusMinutes = snapshot.Preferences.FocusMinutes.ToString(CultureInfo.InvariantCulture);
        ShortBreakMinutes = snapshot.Preferences.ShortBreakMinutes.ToString(CultureInfo.InvariantCulture);
        LongBreakMinutes = snapshot.Preferences.LongBreakMinutes.ToString(CultureInfo.InvariantCulture);
        Apply(snapshot);
    }
    public async Task PollAsync()
    {
        if (busy || polling) return; polling = true; var action = operation;
        try { var context = Capture(); var snapshot = await focus.ReadAsync(context.Scope); Ensure(context); if (action == operation) Apply(snapshot); }
        catch (ApiClientException exception) { if (Workspace == HierarchyWorkspace.SyncedAccount) Clear(); Error = exception.Message; }
        catch (Exception) { Error = "Couldn't refresh focus history. Tracking may be interrupted; use Refresh to review saved checkpoints."; }
        finally { polling = false; }
    }
    private void Apply(FocusSnapshot snapshot)
    {
        canControl = snapshot.CanControl; otherScopeActive = snapshot.OtherScopeActive; Active = snapshot.Active;
        var recent = snapshot.History.Take(200).ToArray(); UpdateRows(History, recent.Select(row => new FocusHistoryRow(row)).ToArray());
        var totals = new List<FocusTaskTotal>();
        foreach (var group in snapshot.History.Where(e => e.Session.Kind == FocusKind.Focus).GroupBy(e => e.Session.TaskId!.Value).OrderBy(g => g.First().Session.Title, StringComparer.OrdinalIgnoreCase))
            totals.Add(new(group.First().Session.Title + " (" + group.Key.ToString("N")[..8] + ")", Duration(FocusRules.TimeSpentTicks(group))));
        UpdateRows(TaskTotals, totals);
        var selectedId = SelectedTask?.Id;
        for (var i = 0; i < Tasks.Count; i++)
        {
            var spent = Duration(FocusRules.TimeSpentTicks(snapshot.History, Tasks[i].Id));
            if (Tasks[i].TimeSpent != spent) Tasks[i] = Tasks[i] with { TimeSpent = spent };
        }
        if (selectedId is { } id) SelectedTask = Tasks.FirstOrDefault(t => t.Id == id);
        Totals = "Workspace TimeSpent: " + Duration(FocusRules.TimeSpentTicks(snapshot.History)) + " (focus only; breaks excluded). " +
            snapshot.History.Count + " sessions saved; showing the latest " + recent.Length + ".";
        var sinceLongBreak = snapshot.History.TakeWhile(e => !(e.Session.Kind == FocusKind.LongBreak && e.Session.Status == FocusStatus.Completed))
            .Count(e => e.Session.Kind == FocusKind.Focus && e.Session.Status == FocusStatus.Completed);
        Suggestion = sinceLongBreak >= 4 ? "At least four focus blocks completed since the last completed long break. Consider a long break; start it manually." :
            $"{sinceLongBreak} completed focus blocks since the last completed long break. Breaks start manually.";
        Status = !canControl ? "Another app instance controls focus tracking. History is readable; close that instance and Refresh to take control." :
            otherScopeActive ? "A session is active in another workspace. Return there and finish it before starting here." :
            Active is not null ? "One shared session across app windows. Leaving this page does not stop it; switching desktop apps can keep counting." : "Choose a pending task for focus, or start a break. Finishing focus does not complete the task.";
        if (focus.Notice.Length > 0) Status += " " + focus.Notice;
        Notify();
    }
    private FocusPreferences Draft()
    {
        static int Parse(string text) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : 0;
        return new FocusPreferences(Parse(FocusMinutes), Parse(ShortBreakMinutes), Parse(LongBreakMinutes)).Validate();
    }
    private async Task StartAsync(FocusKind kind)
    {
        var context = Capture(); var preferences = Draft(); var taskId = SelectedTask?.Id;
        await focus.StartAsync(context.Scope, kind, taskId, preferences); Ensure(context); await RefreshAsync();
    }
    private async Task ControlAsync(string action)
    {
        var context = Capture(); var id = Active!.Session.Id;
        if (action == "pause") await focus.PauseAsync(context.Scope, id);
        else if (action == "resume") await focus.ResumeAsync(context.Scope, id);
        else await focus.StopAsync(context.Scope, id);
        Ensure(context); var snapshot = await focus.ReadAsync(context.Scope); Ensure(context); Apply(snapshot);
    }
    private void Clear()
    {
        generation++; operation++; Tasks.Clear(); History.Clear(); TaskTotals.Clear(); SelectedTask = null; Active = null; canControl = false; otherScopeActive = false;
        FocusMinutes = "25"; ShortBreakMinutes = "5"; LongBreakMinutes = "15"; Totals = ""; Suggestion = ""; Error = ""; Status = "Workspace/account changed. Refresh to load its local history."; Notify();
    }
    private void SessionChanged()
    { if (observedVersion == session.Version) return; observedVersion = session.Version; if (Workspace == HierarchyWorkspace.SyncedAccount) Clear(); }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(HasActive));
        foreach (var command in new[] { RefreshCommand, StartFocusCommand, StartShortBreakCommand, StartLongBreakCommand, PauseCommand, ResumeCommand, StopCommand }) command?.NotifyCanExecuteChanged();
    }
    public static string Duration(long ticks)
    { var span = TimeSpan.FromTicks(ticks); return $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"; }
    private static void UpdateRows<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        for (var i = 0; i < values.Count; i++)
            if (i >= target.Count) target.Add(values[i]);
            else if (!EqualityComparer<T>.Default.Equals(target[i], values[i])) target[i] = values[i];
        while (target.Count > values.Count) target.RemoveAt(target.Count - 1);
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
