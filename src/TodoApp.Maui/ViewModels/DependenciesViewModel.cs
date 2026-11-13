using System.Collections.ObjectModel;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed record DependencyChoice(Guid Id, string Label);
public sealed record DependencyTaskRow(Guid Id, string Title, SyncTaskData? Local, SyncEntry? Remote, bool Conflict, string Status)
{
    public string LocalDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).LocalDetails;
    public string ServerDetails => new HierarchyRow(Id, Title, null, 1, Local is not null, Conflict, Local, Remote, Status).ServerDetails;
}

public sealed class DependenciesViewModel : ObservableViewModel, IDisposable
{
    private readonly ILocalTaskService device;
    private readonly SqliteSyncStore sync;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private bool busy;
    private int generation, observedVersion;
    private HierarchyWorkspace workspace;
    private DependencyTaskRow? selected;
    private DependencyChoice? candidate, selectedLink;
    private string status = "Refresh, select a task, and review its prerequisites.", error = "";
    public ObservableCollection<DependencyTaskRow> Tasks { get; } = new();
    public ObservableCollection<DependencyChoice> Choices { get; } = new();
    public ObservableCollection<DependencyChoice> Draft { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == HierarchyWorkspace.Device || session.Current is not null);
    public bool HasSelection => selected is not null;
    public bool HasConflict => selected?.Conflict == true;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public DependencyChoice? Candidate { get => candidate; set { SetProperty(ref candidate, value); Notify(); } }
    public DependencyChoice? SelectedLink { get => selectedLink; set { SetProperty(ref selectedLink, value); Notify(); } }
    public DependencyTaskRow? SelectedTask
    {
        get => selected;
        set
        {
            SetProperty(ref selected, value); Draft.Clear(); Choices.Clear(); Candidate = null; SelectedLink = null;
            if (value?.Local is { } data)
            {
                foreach (var id in data.Prerequisites ?? TaskPrerequisites.Empty) Draft.Add(Choice(id));
                foreach (var task in Tasks.Where(t => t.Id != value.Id && t.Local is not null).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id))
                    Choices.Add(Choice(task.Id));
            }
            Notify();
        }
    }
    private DependencyChoice Choice(Guid id)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == id);
        return new(id, $"{task?.Title ?? "Missing task"} ({id.ToString("N")[..8]}) - " +
            (task?.Local is not { } data ? "not cached/live; sync and review" : data.IsCompleted ? "Completed" : "Pending"));
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand ClearCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand KeepLocalCommand { get; }
    public AsyncCommand UseServerCommand { get; }

    public DependenciesViewModel(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, ITaskNavigation navigation)
    {
        this.device = device; this.sync = sync; this.session = session; this.navigation = navigation; observedVersion = session.Version;
        RefreshCommand = Command(LoadAsync);
        AddCommand = Command(() =>
        {
            if (Draft.Count >= TaskPrerequisites.MaxCount) throw new TaskDependencyException("Choose at most 32 prerequisites.");
            Draft.Add(Candidate!); Notify(); return Task.CompletedTask;
        }, () => selected?.Local is not null && Candidate is { } item && !Draft.Any(p => p.Id == item.Id));
        RemoveCommand = Command(() => { Draft.Remove(SelectedLink!); SelectedLink = null; Notify(); return Task.CompletedTask; }, () => SelectedLink is not null);
        ClearCommand = Command(() => { Draft.Clear(); SelectedLink = null; Notify(); return Task.CompletedTask; }, () => Draft.Count > 0);
        SaveCommand = Command(() => SaveAsync(false), () => selected is { Local: not null, Conflict: false });
        KeepLocalCommand = Command(() => SaveAsync(true), () => selected is { Local: not null, Conflict: true });
        UseServerCommand = Command(UseServerAsync, () => Workspace == HierarchyWorkspace.SyncedAccount && HasConflict);
        session.Changed += SessionChanged;
    }
    private sealed record Context(string Scope, HierarchyWorkspace Workspace, int Generation, int Version);
    private Context Capture()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a workspace.");
        var scope = Workspace == HierarchyWorkspace.Device ? "device" : session.Current is { } saved
            ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in and sync this account first.");
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
        catch (Exception) { Error = "Couldn't finish the dependency action. Saved work remains; refresh and review before retrying."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the dependency action.");
    private async Task LoadAsync()
    {
        var context = Capture(); Tasks.Clear(); SelectedTask = null;
        var loaded = new List<DependencyTaskRow>();
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            var tasks = await device.GetTasksAsync(); Ensure(context);
            loaded.AddRange(tasks.Select(t => new DependencyTaskRow(t.Id, t.Title,
                new(t.Title, t.Description, t.DueDate, t.Priority, t.Category?.Name, t.IsCompleted, t.Tags, new(t.ParentTaskId), t.Prerequisites, t.Quadrant, t.Recurrence), null, false, "Device")));
        }
        else
        {
            var state = await sync.ReadAsync(context.Scope); Ensure(context);
            loaded.AddRange(state.Rows.Values.Where(r => r.Local is not null || r.Pending is not null || r.Conflict)
                .Select(r => new DependencyTaskRow(r.Id, r.Title, r.Local, r.Remote, r.Conflict, r.Summary)));
        }
        var nodes = loaded.Where(r => r.Local is not null).Select(r => new DependencyNode(r.Id, r.Local!.Prerequisites ?? TaskPrerequisites.Empty, r.Local.IsCompleted)).ToArray();
        Ensure(context);
        foreach (var row in loaded.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id))
        {
            var dependencies = row.Local?.Prerequisites ?? TaskPrerequisites.Empty;
            var count = TaskDependencies.BlockingCount(nodes, dependencies);
            Tasks.Add(row with { Status = row.Status + (row.Local is null ? " | Deletion intent" :
                $" | {dependencies.Count} prerequisites; {count} unfinished/missing" +
                (row.Local.IsCompleted ? " | Completed (reopened prerequisites do not undo completion)" : count > 0 ? " | Completion blocked" : " | Ready to complete")) });
        }
        Status = $"{Tasks.Count} tasks. Add/remove links, then Save. Selecting another task or refreshing reloads saved links. " +
            (Workspace == HierarchyWorkspace.Device ? "Device changes save immediately." : "Account changes queue locally; open Synced tasks to sync.");
    }
    private async Task SaveAsync(bool keepLocal)
    {
        var context = Capture(); var row = SelectedTask!; var prerequisites = TaskPrerequisites.FromIds(Draft.Select(p => p.Id));
        if (keepLocal && !await navigation.ConfirmDependencyConflictAsync(row.Title)) return;
        Ensure(context);
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            if (!await device.SetPrerequisitesAsync(row.Id, prerequisites)) throw new TaskDependencyException("The task no longer exists. Refresh.");
        }
        else await sync.SetPrerequisitesAsync(context.Scope, row.Id, prerequisites, row.Remote?.Revision ?? 0, row.Local!, keepLocal);
        Ensure(context); await LoadAsync();
    }
    private async Task UseServerAsync()
    {
        var context = Capture(); var row = SelectedTask!;
        await sync.ResolveAsync(context.Scope, row.Id, false, row.Remote?.Revision ?? 0); Ensure(context); await LoadAsync();
    }
    private void Clear()
    { generation++; Tasks.Clear(); SelectedTask = null; Error = ""; Status = "Workspace/account changed. Refresh to load dependencies."; Notify(); }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return; observedVersion = session.Version;
        if (Workspace == HierarchyWorkspace.SyncedAccount) Clear();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasConflict));
        foreach (var command in new[] { RefreshCommand, AddCommand, RemoveCommand, ClearCommand, SaveCommand, KeepLocalCommand, UseServerCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
