using System.Collections.ObjectModel;
using TodoApp.Domain.Services;
using TodoApp.Domain.Views;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public enum TaskViewWorkspace { Device, SyncedAccount }
public sealed class TaskViewsViewModel : ObservableViewModel, IDisposable
{
    private readonly SqliteTaskViewStore views;
    private readonly ILocalTaskService local;
    private readonly SqliteSyncStore sync;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private readonly TimeProvider clock;
    private bool busy;
    private int generation;
    private int observedVersion;
    private TaskViewWorkspace workspace;
    private SavedTaskView? selectedView;
    private TaskViewRow? selectedTask;
    private TaskViewDefinition applied = new();
    private string search = "", categoryName = "", viewName = "", error = "", status = "Choose filters, then Apply. Saved views contain rules, not copies of tasks.";
    private ViewCompletion completion;
    private ViewPriority priority;
    private ViewCategory category;
    private ViewDue due;
    private ViewSort sort;
    public ObservableCollection<SavedTaskView> SavedViews { get; } = new();
    public ObservableCollection<TaskViewRow> Results { get; } = new();
    public IReadOnlyList<TaskViewWorkspace> Workspaces { get; } = Enum.GetValues<TaskViewWorkspace>();
    public IReadOnlyList<ViewCompletion> Completions { get; } = Enum.GetValues<ViewCompletion>();
    public IReadOnlyList<ViewPriority> Priorities { get; } = Enum.GetValues<ViewPriority>();
    public IReadOnlyList<ViewCategory> Categories { get; } = Enum.GetValues<ViewCategory>();
    public IReadOnlyList<ViewDue> DueOptions { get; } = Enum.GetValues<ViewDue>();
    public IReadOnlyList<ViewSort> SortOptions { get; } = Enum.GetValues<ViewSort>();
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == TaskViewWorkspace.Device || session.Current is not null);
    public bool HasSelection => SelectedTask is not null;
    public string OpenText => Workspace == TaskViewWorkspace.Device ? "Edit selected device task" : "Open Synced tasks workspace";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string TodayText => $"Date filters use this device's day: {DateOnly.FromDateTime(clock.GetLocalNow().DateTime):d}. NextSevenDays includes today plus six days.";
    public string Search { get => search; set => SetProperty(ref search, value ?? ""); }
    public string CategoryName { get => categoryName; set => SetProperty(ref categoryName, value ?? ""); }
    public string ViewName { get => viewName; set => SetProperty(ref viewName, value ?? ""); }
    public ViewCompletion Completion { get => completion; set => SetProperty(ref completion, value); }
    public ViewPriority Priority { get => priority; set => SetProperty(ref priority, value); }
    public ViewCategory Category { get => category; set => SetProperty(ref category, value); }
    public ViewDue Due { get => due; set => SetProperty(ref due, value); }
    public ViewSort Sort { get => sort; set => SetProperty(ref sort, value); }
    public TaskViewWorkspace Workspace
    {
        get => workspace;
        set { if (SetProperty(ref workspace, value)) ClearWorkspace(); }
    }
    public SavedTaskView? SelectedView
    {
        get => selectedView;
        set { SetProperty(ref selectedView, value); if (value is not null) { SetDraft(value.Definition); ViewName = value.Name; } Notify(); }
    }
    public TaskViewRow? SelectedTask
    {
        get => selectedTask;
        set { SetProperty(ref selectedTask, value); Notify(); }
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand UpdateCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public TaskViewsViewModel(SqliteTaskViewStore views, ILocalTaskService local, SqliteSyncStore sync,
        ClientSession session, ITaskNavigation navigation, TimeProvider? clock = null)
    {
        this.views = views; this.local = local; this.sync = sync; this.session = session; this.navigation = navigation;
        this.clock = clock ?? TimeProvider.System; observedVersion = session.Version;
        RefreshCommand = Command(RefreshAsync);
        ApplyCommand = Command(async () => { var definition = Draft().Normalize(); await LoadResultsAsync(Context(), definition); applied = definition; });
        ResetCommand = Command(async () => { SelectedView = null; ViewName = ""; SetDraft(new()); await LoadResultsAsync(Context(), new()); applied = new(); });
        CreateCommand = Command(() => SaveAsync(create: true));
        UpdateCommand = Command(() => SaveAsync(create: false), () => SelectedView is not null);
        DeleteCommand = Command(DeleteAsync, () => SelectedView is not null);
        OpenCommand = Command(OpenAsync, () => SelectedTask is not null);
        session.Changed += SessionChanged;
    }
    private TaskViewDefinition Draft() => new(Search, Completion, Priority, Category, CategoryName, Due, Sort);
    private void SetDraft(TaskViewDefinition value)
    {
        Search = value.Search; Completion = value.Completion; Priority = value.Priority; Category = value.Category;
        CategoryName = value.CategoryName; Due = value.Due; Sort = value.Sort;
    }
    private sealed record OperationContext(string Scope, int Generation, int SessionVersion, TaskViewWorkspace Workspace);
    private OperationContext Context()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a workspace.");
        var scope = Workspace == TaskViewWorkspace.Device ? "device" : session.Current is { } saved
            ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in through Account & server to select an account cache.");
        return new(scope, generation, session.Version, Workspace);
    }
    private void Ensure(OperationContext context)
    {
        if (context.Generation != generation || context.Workspace != Workspace)
            throw new ApiClientException("The workspace changed. The old result was discarded.");
        if (context.Workspace == TaskViewWorkspace.SyncedAccount) session.EnsureVersion(context.SessionVersion);
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (ApiClientException exception) { Error = exception.Message; }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (SavedViewConflictException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't load/save this view. Task data is unchanged; keep your filter draft and retry."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the saved-view request.");
    private async Task RefreshAsync()
    {
        var context = Context(); Results.Clear(); SelectedTask = null; Status = "Loading saved views and tasks...";
        var loaded = await views.ReadAsync(context.Scope); Ensure(context);
        SavedViews.Clear(); SelectedView = null;
        foreach (var view in loaded) SavedViews.Add(view);
        await LoadResultsAsync(context, applied);
    }
    private async Task LoadResultsAsync(OperationContext context, TaskViewDefinition definition)
    {
        Results.Clear(); SelectedTask = null; Status = "Loading tasks for the applied view...";
        TaskViewRow[] rows;
        var syncSummary = "";
        if (context.Workspace == TaskViewWorkspace.Device)
        {
            var tasks = await local.GetTasksAsync(); Ensure(context);
            rows = tasks.Select(t => new TaskViewRow(new(t.Id, t.Title, t.Description, t.IsCompleted, t.DueDate, t.Priority, t.Category?.Name))).ToArray();
        }
        else
        {
            var state = await sync.ReadAsync(context.Scope); Ensure(context);
            rows = state.Rows.Values.Where(r => r.Local is not null).Select(r =>
                new TaskViewRow(new(r.Id, r.Local!.Title, r.Local.Description, r.Local.IsCompleted, r.Local.DueDate, r.Local.Priority, r.Local.CategoryName), r.Summary)).ToArray();
            syncSummary = $" {state.Rows.Values.Count(r => r.Pending is not null)} queued; {state.Rows.Values.Count(r => r.Conflict)} conflicts across the whole cache. Resolve them in Synced tasks.";
        }
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var matches = TaskViewQueries.Apply(rows.Select(r => r.Task), definition, today);
        var byId = rows.ToDictionary(r => r.Id); Ensure(context);
        foreach (var match in matches) Results.Add(byId[match.Id]);
        Status = $"{Results.Count} of {rows.Length} tasks match applied filters. Workspace: {(context.Workspace == TaskViewWorkspace.Device ? "device" : session.Current!.User.Email + " at " + session.Current.Origin)}." + syncSummary;
        OnPropertyChanged(nameof(TodayText));
    }
    private async Task SaveAsync(bool create)
    {
        var context = Context(); var definition = Draft().Normalize();
        var selected = SelectedView;
        if (!create && selected is null) throw new ArgumentException("Select a saved view to update.");
        var saved = create ? await views.CreateAsync(context.Scope, ViewName, definition)
            : await views.UpdateAsync(context.Scope, selected!.Id, selected.Revision, ViewName, definition);
        Ensure(context);
        var loaded = await views.ReadAsync(context.Scope); Ensure(context);
        SavedViews.Clear(); foreach (var view in loaded) SavedViews.Add(view);
        SelectedView = SavedViews.Single(v => v.Id == saved.Id);
        await LoadResultsAsync(context, definition); applied = definition;
    }
    private async Task DeleteAsync()
    {
        var context = Context(); var selected = SelectedView!;
        if (!await navigation.ConfirmDeleteViewAsync(selected.Name)) return;
        Ensure(context); await views.DeleteAsync(context.Scope, selected.Id, selected.Revision); Ensure(context);
        SelectedView = null; ViewName = "";
        await RefreshAsync(); // Definition removal never removes tasks or changes applied results.
    }
    private async Task OpenAsync()
    {
        var context = Context(); var id = SelectedTask!.Id; Ensure(context);
        if (context.Workspace == TaskViewWorkspace.Device) await navigation.EditTaskAsync(id);
        else await navigation.OpenSyncAsync();
    }
    private void ClearWorkspace()
    {
        generation++; Results.Clear(); SavedViews.Clear(); SelectedTask = null; SelectedView = null;
        ViewName = ""; SetDraft(new()); applied = new(); Error = "";
        Status = "Workspace changed. Refresh to load its tasks and saved views."; Notify();
    }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return;
        observedVersion = session.Version;
        if (Workspace == TaskViewWorkspace.SyncedAccount) ClearWorkspace();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(OpenText));
        foreach (var command in new[] { RefreshCommand, ApplyCommand, ResetCommand, CreateCommand, UpdateCommand, DeleteCommand, OpenCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
