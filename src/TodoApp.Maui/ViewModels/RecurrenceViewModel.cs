using System.Collections.ObjectModel;
using System.Globalization;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed record RepeatOption(RepeatFrequency? Value, string Label);
public sealed record RecurringRow(Guid Id, string Title, SyncTaskData? Local, SyncEntry? Remote, bool Conflict, bool Pending, string Status)
{
    public string RecurrenceText => RecurrenceRules.Describe((Local ?? Remote?.Task)?.Recurrence);
    public string LocalDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).LocalDetails;
    public string ServerDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).ServerDetails;
}

public sealed class RecurrenceViewModel : ObservableViewModel, IDisposable
{
    private readonly ILocalTaskService device;
    private readonly SqliteSyncStore sync;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private bool busy;
    private int generation, observedVersion;
    private HierarchyWorkspace workspace;
    private RecurringRow? selected;
    private RepeatOption? frequency;
    private string interval = "1", status = "Refresh to configure repeating tasks.", error = "";
    private DateTime? anchorDate = DateTime.Today;
    public ObservableCollection<RecurringRow> Tasks { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public IReadOnlyList<RepeatOption> Frequencies { get; } = new RepeatOption[] { new(null, "Not repeating"), new(RepeatFrequency.Daily, "Daily"), new(RepeatFrequency.Weekly, "Weekly"), new(RepeatFrequency.Monthly, "Monthly") };
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == HierarchyWorkspace.Device || session.Current is not null);
    public bool HasSelection => selected is not null;
    public bool HasConflict => selected?.Conflict == true;
    public RepeatOption? Frequency { get => frequency; set { SetProperty(ref frequency, value); PreviewChanged(); } }
    public string Interval { get => interval; set { SetProperty(ref interval, value ?? ""); PreviewChanged(); } }
    public DateTime? AnchorDate { get => anchorDate; set { SetProperty(ref anchorDate, value); PreviewChanged(); } }
    public string Preview
    {
        get
        {
            if (selected?.Local is not { } local) return "Select a live task.";
            try
            {
                var current = CurrentForConfiguration(selected);
                var next = RecurrenceRules.Configure(current, Draft());
                return next.IsActive ? RecurrenceRules.Describe(next) + ". Completion advances one occurrence; the task stays pending." : "Stop repeating; retain the due date and consumed occurrence count.";
            }
            catch (ArgumentException exception) { return exception.Message; }
        }
    }
    public RecurringRow? SelectedTask
    {
        get => selected;
        set
        {
            SetProperty(ref selected, value);
            var rule = value?.Local?.Recurrence?.Rule;
            Frequency = Frequencies.FirstOrDefault(f => f.Value == rule?.Frequency);
            Interval = (rule?.Interval ?? 1).ToString(CultureInfo.InvariantCulture);
            AnchorDate = (rule?.AnchorDate ?? value?.Local?.DueDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            Notify();
        }
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CompleteCommand { get; }
    public AsyncCommand KeepLocalCommand { get; }
    public AsyncCommand UseServerCommand { get; }
    public RecurrenceViewModel(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, ITaskNavigation navigation)
    {
        this.device = device; this.sync = sync; this.session = session; this.navigation = navigation; observedVersion = session.Version;
        RefreshCommand = Command(LoadAsync);
        SaveCommand = Command(() => SaveAsync(false), () => selected is { Local: not null, Conflict: false } && Frequency is not null);
        CompleteCommand = Command(CompleteAsync, () => selected is { Local.Recurrence.IsActive: true, Conflict: false, Pending: false });
        KeepLocalCommand = Command(() => SaveAsync(true), () => selected is { Local: not null, Conflict: true } && Frequency is not null);
        UseServerCommand = Command(UseServerAsync, () => Workspace == HierarchyWorkspace.SyncedAccount && HasConflict);
        session.Changed += SessionChanged;
    }
    private RepeatRule? Draft()
    {
        if (Frequency is null) throw new ArgumentException("Choose a repeat frequency.");
        if (Frequency.Value is not { } repeat) return null;
        if (!int.TryParse(Interval.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var step)) throw new ArgumentException("Enter an interval from 1 to 365.");
        var rule = new RepeatRule(repeat, step, DateOnly.FromDateTime(AnchorDate ?? throw new ArgumentException("Choose an anchor date."))); RecurrenceRules.ValidateRule(rule); return rule;
    }
    private static RepeatState CurrentForConfiguration(RecurringRow row) => row.Conflict
        ? row.Remote?.Task?.Recurrence ?? new RepeatState(null, row.Remote?.RecurrenceFloor ?? 0, row.Remote?.RecurrenceFloor ?? 0)
        : row.Local?.Recurrence ?? RepeatState.None;
    private sealed record Context(string Scope, HierarchyWorkspace Workspace, int Generation, int Version);
    private Context Capture()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a workspace.");
        var scope = Workspace == HierarchyWorkspace.Device ? "device" : session.Current is { } saved
            ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in and sync the account first.");
        return new(scope, Workspace, generation, session.Version);
    }
    private void Ensure(Context context)
    {
        if (context.Workspace != Workspace || context.Generation != generation) throw new ApiClientException("Workspace changed; old results were discarded.");
        if (context.Workspace == HierarchyWorkspace.SyncedAccount) session.EnsureVersion(context.Version);
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (ApiClientException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't finish the recurrence action. Saved work remains; refresh and review before retrying."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the recurrence action.");
    private async Task LoadAsync()
    {
        var context = Capture(); Tasks.Clear(); SelectedTask = null;
        var rows = new List<RecurringRow>();
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            var tasks = await device.GetTasksAsync(); Ensure(context);
            rows.AddRange(tasks.Select(t => new RecurringRow(t.Id, t.Title,
                new(t.Title, t.Description, t.DueDate, t.Priority, t.Category?.Name, t.IsCompleted, t.Tags, new(t.ParentTaskId), t.Prerequisites, t.Quadrant, t.Recurrence), null, false, false,
                t.IsCompleted ? "Completed: reopen before enabling recurrence" : "Pending")));
        }
        else
        {
            var state = await sync.ReadAsync(context.Scope); Ensure(context);
            rows.AddRange(state.Rows.Values.Where(r => r.Local is not null || r.Pending is not null || r.Conflict)
                .Select(r => new RecurringRow(r.Id, r.Title, r.Local, r.Remote, r.Conflict, r.Pending is not null, r.Summary)));
        }
        Ensure(context);
        foreach (var row in rows.OrderByDescending(r => r.Local?.Recurrence?.IsActive == true).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id)) Tasks.Add(row);
        Status = $"{rows.Count} tasks. Selecting another task or refreshing discards an unsaved rule draft. " +
            (Workspace == HierarchyWorkspace.Device ? "Device changes save immediately." : "Account changes queue locally. Sync before completing an occurrence, and again before another completion.");
    }
    private async Task SaveAsync(bool keepLocal)
    {
        var context = Capture(); var row = SelectedTask!; var rule = Draft();
        if (keepLocal && !await navigation.ConfirmRecurrenceConflictAsync(row.Title)) return;
        Ensure(context);
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            if (await device.SetRecurrenceRuleAsync(row.Id, rule) is null) throw new TaskRecurrenceException("The task no longer exists. Refresh.");
        }
        else await sync.SetRecurrenceRuleAsync(context.Scope, row.Id, rule, row.Remote?.Revision ?? 0, row.Local!, keepLocal);
        Ensure(context); await LoadAsync();
    }
    private async Task CompleteAsync()
    {
        var context = Capture(); var row = SelectedTask!; var current = row.Local!.Recurrence ?? RepeatState.None;
        var next = RecurrenceRules.Complete(current, current);
        if (!await navigation.ConfirmOccurrenceAsync(row.Title, RecurrenceRules.DueDate(current), RecurrenceRules.DueDate(next))) return;
        Ensure(context);
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            if (await device.CompleteOccurrenceAsync(row.Id, current) is null) throw new TaskRecurrenceException("The task no longer exists. Refresh.");
        }
        else await sync.CompleteOccurrenceAsync(context.Scope, row.Id, row.Remote?.Revision ?? 0, row.Local);
        Ensure(context); await LoadAsync();
    }
    private async Task UseServerAsync()
    {
        var context = Capture(); var row = SelectedTask!;
        await sync.ResolveAsync(context.Scope, row.Id, false, row.Remote?.Revision ?? 0); Ensure(context); await LoadAsync();
    }
    private void Clear()
    { generation++; Tasks.Clear(); SelectedTask = null; Error = ""; Status = "Workspace/account changed. Refresh to load recurrence."; Notify(); }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return; observedVersion = session.Version;
        if (Workspace == HierarchyWorkspace.SyncedAccount) Clear();
    }
    private void PreviewChanged() { OnPropertyChanged(nameof(Preview)); Notify(); }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasConflict)); OnPropertyChanged(nameof(Preview));
        foreach (var command in new[] { RefreshCommand, SaveCommand, CompleteCommand, KeepLocalCommand, UseServerCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
