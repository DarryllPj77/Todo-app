using System.Collections.ObjectModel;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public enum HierarchyWorkspace { Device, SyncedAccount }
public sealed class HierarchyViewModel : ObservableViewModel, IDisposable
{
    private readonly ILocalTaskService device;
    private readonly SqliteSyncStore sync;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private bool busy;
    private int generation, observedVersion;
    private HierarchyWorkspace workspace;
    private HierarchyRow? selected;
    private ParentChoice? parent;
    private string status = "Refresh to inspect task nesting. Parent completion does not complete children.", error = "";
    public ObservableCollection<HierarchyRow> Rows { get; } = new();
    public ObservableCollection<ParentChoice> Parents { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public bool IsIdle => !busy;
    public bool Available => !busy && (Workspace == HierarchyWorkspace.Device || session.Current is not null);
    public bool HasSelection => SelectedTask is not null;
    public bool HasConflict => SelectedTask?.Conflict == true;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public ParentChoice? SelectedParent { get => parent; set { SetProperty(ref parent, value); Notify(); } }
    public HierarchyRow? SelectedTask
    {
        get => selected;
        set
        {
            SetProperty(ref selected, value); Parents.Clear();
            Parents.Add(new(null, "Root task (no parent)"));
            if (value is not null)
                foreach (var row in Rows.Where(r => r.Live && r.Id != value.Id && !Descendant(r.Id, value.Id)).OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id))
                    Parents.Add(new(row.Id, row.ShortTitle));
            SelectedParent = Parents.FirstOrDefault(p => p.Id == value?.ParentId) ?? Parents[0]; Notify();
        }
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand MoveCommand { get; }
    public AsyncCommand KeepLocalMoveCommand { get; }
    public AsyncCommand UseServerCommand { get; }
    public HierarchyViewModel(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, ITaskNavigation navigation)
    {
        this.device = device; this.sync = sync; this.session = session; this.navigation = navigation; observedVersion = session.Version;
        RefreshCommand = Command(LoadAsync);
        MoveCommand = Command(() => MoveAsync(false), () => SelectedTask is { Live: true, Conflict: false } && SelectedParent is not null);
        KeepLocalMoveCommand = Command(() => MoveAsync(true), () => SelectedTask is { Live: true, Conflict: true } && SelectedParent is not null);
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
        if (Workspace == HierarchyWorkspace.SyncedAccount) session.EnsureVersion(context.Version);
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (ApiClientException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't update the hierarchy. Saved work remains; refresh and review before retrying."; }
        finally { busy = false; Notify(); }
    }, () => Available && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the hierarchy action.");
    private async Task LoadAsync()
    {
        var context = Capture(); Rows.Clear(); SelectedTask = null; Status = "Loading this workspace's hierarchy...";
        var loaded = new List<HierarchyRow>();
        if (Workspace == HierarchyWorkspace.Device)
        {
            var tasks = await device.GetTasksAsync(); Ensure(context);
            loaded.AddRange(tasks.Select(t => new HierarchyRow(t.Id, t.Title, t.ParentTaskId, 1, true, false,
                new(t.Title,t.Description,t.DueDate,t.Priority,t.Category?.Name,t.IsCompleted,t.Tags,new TaskParent(t.ParentTaskId)), null,
                t.IsCompleted ? "Completed (children keep their own completion state)" : "Pending")));
        }
        else
        {
            var state = await sync.ReadAsync(context.Scope); Ensure(context);
            loaded.AddRange(state.Rows.Values.Where(r => r.Local is not null || r.Pending is not null || r.Conflict).Select(r =>
                new HierarchyRow(r.Id, r.Title, (r.Local ?? r.Remote?.Task)?.Parent?.Id, 1, r.Local is not null, r.Conflict,
                    r.Local, r.Remote, r.Summary)));
        }
        Ensure(context);
        // Flatten defensively: partial downloads/corruption must not loop the UI.
        var visited = new HashSet<Guid>(); var map = loaded.ToDictionary(r => r.Id);
        void Add(HierarchyRow root)
        {
            var stack = new Stack<(HierarchyRow Row, int Level)>(); stack.Push((root, 1));
            while (stack.TryPop(out var item))
            {
                if (!visited.Add(item.Row.Id)) continue;
                var missing = item.Row.ParentId is { } id && (!map.TryGetValue(id, out var parentRow) || !parentRow.Live);
                Rows.Add(item.Row with { Level = item.Level, Status = item.Row.Status + (missing ? " | Parent not cached/live; sync and review" : "") });
                foreach (var child in loaded.Where(r => r.ParentId == item.Row.Id).OrderByDescending(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Id))
                    stack.Push((child, item.Level + 1));
            }
        }
        foreach (var root in loaded.Where(r => r.ParentId is null || !map.ContainsKey(r.ParentId.Value)).OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id)) Add(root);
        foreach (var leftover in loaded.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id)) Add(leftover);
        Status = $"{Rows.Count} tasks shown. Max 16 levels; move/delete children before deleting a parent. " +
            (Workspace == HierarchyWorkspace.Device ? "Device moves save immediately." : "Account moves queue locally. Sync in Synced tasks; parent/child uploads are ordered.");
    }
    private bool Descendant(Guid candidate, Guid ancestor)
    {
        var map = Rows.ToDictionary(r => r.Id); var seen = new HashSet<Guid>(); Guid? current = candidate;
        while (current is { } id && seen.Add(id) && map.TryGetValue(id, out var row))
        { if (id == ancestor) return true; current = row.ParentId; }
        return false;
    }
    private async Task MoveAsync(bool keepLocal)
    {
        var context = Capture(); var row = SelectedTask!; var parentId = SelectedParent!.Id;
        if (keepLocal && !await navigation.ConfirmHierarchyConflictAsync(row.Title)) return;
        Ensure(context);
        if (context.Workspace == HierarchyWorkspace.Device)
        {
            if (!await device.SetParentAsync(row.Id, parentId)) throw new TaskHierarchyException("The task no longer exists. Refresh.");
        }
        else await sync.ReparentAsync(context.Scope, row.Id, parentId, row.Remote?.Revision ?? 0, row.Local!, keepLocal);
        Ensure(context); await LoadAsync();
    }
    private async Task UseServerAsync()
    {
        var context = Capture(); var row = SelectedTask!;
        await sync.ResolveAsync(context.Scope, row.Id, false, row.Remote?.Revision ?? 0); Ensure(context); await LoadAsync();
    }
    private void Clear()
    { generation++; Rows.Clear(); SelectedTask = null; Error = ""; Status = "Workspace/account changed. Refresh to load its hierarchy."; Notify(); }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return; observedVersion = session.Version;
        if (Workspace == HierarchyWorkspace.SyncedAccount) Clear();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasConflict));
        foreach (var command in new[] { RefreshCommand, MoveCommand, KeepLocalMoveCommand, UseServerCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
