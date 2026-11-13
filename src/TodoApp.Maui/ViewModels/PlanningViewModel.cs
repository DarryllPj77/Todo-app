using System.Collections.ObjectModel;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed record PlanningRow(Guid Id, string Title, SyncTaskData? Local, SyncEntry? Remote, bool Conflict, string Status)
{
    public EisenhowerQuadrant Quadrant => (Local ?? Remote?.Task)?.Quadrant ?? EisenhowerQuadrant.Unclassified;
    public string QuadrantLabel => Eisenhower.Label(Quadrant);
    public string LocalDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).LocalDetails;
    public string ServerDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).ServerDetails;
}

public sealed class PlanningViewModel : ObservableViewModel, IDisposable
{
    private readonly ILocalTaskService device;
    private readonly SqliteSyncStore sync;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private bool busy, includeCompleted;
    private int generation, observedVersion;
    private HierarchyWorkspace workspace;
    private PlanningRow? selected;
    private QuadrantOption? selectedQuadrant;
    private QuadrantOption filter = QuadrantOption.Filters[0];
    private readonly List<PlanningRow> loaded = new();
    private string status = "Refresh to classify tasks manually.", error = "", counts = "";
    public ObservableCollection<PlanningRow> Tasks { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public IReadOnlyList<QuadrantOption> Quadrants => QuadrantOption.Classifications;
    public IReadOnlyList<QuadrantOption> Filters => QuadrantOption.Filters;
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == HierarchyWorkspace.Device || session.Current is not null);
    public bool HasSelection => selected is not null;
    public bool HasConflict => selected?.Conflict == true;
    public bool IncludeCompleted { get => includeCompleted; set { if (SetProperty(ref includeCompleted, value)) ApplyFilter(); } }
    public QuadrantOption Filter { get => filter; set { if (value is not null && SetProperty(ref filter, value)) ApplyFilter(); } }
    public QuadrantOption? SelectedQuadrant { get => selectedQuadrant; set { SetProperty(ref selectedQuadrant, value); Notify(); } }
    public PlanningRow? SelectedTask
    {
        get => selected;
        set { SetProperty(ref selected, value); SelectedQuadrant = value is null ? null : Quadrants.FirstOrDefault(q => q.Value == value.Quadrant); Notify(); }
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Counts { get => counts; private set => SetProperty(ref counts, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand KeepLocalCommand { get; }
    public AsyncCommand UseServerCommand { get; }
    public PlanningViewModel(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, ITaskNavigation navigation)
    {
        this.device = device; this.sync = sync; this.session = session; this.navigation = navigation; observedVersion = session.Version;
        RefreshCommand = Command(LoadAsync);
        SaveCommand = Command(() => SaveAsync(false), () => selected is { Local: not null, Conflict: false } && SelectedQuadrant?.Value is not null);
        KeepLocalCommand = Command(() => SaveAsync(true), () => selected is { Local: not null, Conflict: true } && SelectedQuadrant?.Value is not null);
        UseServerCommand = Command(UseServerAsync, () => Workspace == HierarchyWorkspace.SyncedAccount && HasConflict);
        session.Changed += SessionChanged;
    }
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
        catch (Exception) { Error = "Couldn't finish the planning action. Saved work remains; refresh and review before retrying."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the planning action.");
    private async Task LoadAsync()
    {
        var context = Capture(); loaded.Clear(); Tasks.Clear(); SelectedTask = null;
        var rows = new List<PlanningRow>();
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            var tasks = await device.GetTasksAsync(); Ensure(context);
            rows.AddRange(tasks.Select(t => new PlanningRow(t.Id, t.Title,
                new(t.Title, t.Description, t.DueDate, t.Priority, t.Category?.Name, t.IsCompleted, t.Tags, new(t.ParentTaskId), t.Prerequisites, t.Quadrant, t.Recurrence), null, false,
                t.IsCompleted ? "Completed" : "Pending")));
        }
        else
        {
            var state = await sync.ReadAsync(context.Scope); Ensure(context);
            rows.AddRange(state.Rows.Values.Where(r => r.Local is not null || r.Pending is not null || r.Conflict)
                .Select(r => new PlanningRow(r.Id, r.Title, r.Local, r.Remote, r.Conflict, r.Summary)));
        }
        Ensure(context); loaded.AddRange(rows); ApplyFilter();
        Status = "Choose a task and quadrant, then Save. Unclassified clears the classification. " +
            (context.Workspace == HierarchyWorkspace.Device ? "Device changes save immediately." : "Account changes queue locally; open Synced tasks to sync.");
    }
    private void ApplyFilter()
    {
        Tasks.Clear(); SelectedTask = null;
        foreach (var row in loaded.Where(r => (IncludeCompleted || r.Local?.IsCompleted != true || r.Conflict) && (Filter.Value is null || r.Quadrant == Filter.Value))
            .OrderBy(r => r.Quadrant).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id)) Tasks.Add(row);
        Counts = string.Join("\n", Quadrants.Select(q => q.Label + ": " + loaded.Count(r => r.Local is { IsCompleted: false } && r.Quadrant == q.Value))) +
            $"\n{Tasks.Count} rows shown. Counts cover pending live tasks across all quadrants; conflicts/deletion intent may also appear.";
    }
    private async Task SaveAsync(bool keepLocal)
    {
        var context = Capture(); var row = SelectedTask!; var quadrant = SelectedQuadrant!.Value!.Value;
        if (!Enum.IsDefined(quadrant)) throw new ArgumentException("Choose a valid urgency/importance quadrant.");
        if (keepLocal && !await navigation.ConfirmPlanningConflictAsync(row.Title)) return;
        Ensure(context);
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            if (!await device.SetQuadrantAsync(row.Id, quadrant)) throw new ArgumentException("The task no longer exists. Refresh.");
        }
        else await sync.SetQuadrantAsync(context.Scope, row.Id, quadrant, row.Remote?.Revision ?? 0, row.Local!, keepLocal);
        Ensure(context); await LoadAsync();
    }
    private async Task UseServerAsync()
    {
        var context = Capture(); var row = SelectedTask!;
        await sync.ResolveAsync(context.Scope, row.Id, false, row.Remote?.Revision ?? 0); Ensure(context); await LoadAsync();
    }
    private void Clear()
    { generation++; loaded.Clear(); Tasks.Clear(); SelectedTask = null; Counts = ""; Error = ""; Status = "Workspace/account changed. Refresh to load planning."; Notify(); }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return; observedVersion = session.Version;
        if (Workspace == HierarchyWorkspace.SyncedAccount) Clear();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasConflict));
        foreach (var command in new[] { RefreshCommand, SaveCommand, KeepLocalCommand, UseServerCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
