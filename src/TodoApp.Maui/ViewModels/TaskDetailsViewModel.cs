using System.Collections.ObjectModel;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class TaskDetailsViewModel(WorkspaceTaskReader reader, ILocalTaskService device,
    SqliteSyncStore sync, ClientSession session) : ObservableViewModel, IDisposable
{
    private TaskContext? context;
    private WorkspaceTask? task;
    private string error = "", subtaskTitle = "";
    public TaskContext Context => context ?? throw new InvalidOperationException("Choose a task first.");
    public string Title => task?.Title ?? "Task details";
    public string Summary => task?.Summary ?? "";
    public string Description => task?.Data.Description ?? "No description.";
    public string Classification => TodoApp.Domain.Enums.Eisenhower.Label(task?.Data.Quadrant ?? TodoApp.Domain.Enums.EisenhowerQuadrant.Unclassified);
    public string Repeat => TodoApp.Domain.Recurrence.RecurrenceRules.Describe(task?.Data.Recurrence);
    public bool CanEdit => task is { Conflict: false } &&
        (Context.Workspace == HierarchyWorkspace.Device || session.Version == Context.SessionVersion);
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string SubtaskTitle { get => subtaskTitle; set => SetProperty(ref subtaskTitle, value ?? ""); }
    public ObservableCollection<WorkspaceTask> Subtasks { get; } = new();
    public ObservableCollection<WorkspaceTask> BlockedBy { get; } = new();
    public string Parent { get; private set; } = "No parent";
    public async Task LoadAsync(TaskContext selected)
    {
        context = selected; task = null; Parent = ""; Subtasks.Clear(); BlockedBy.Clear(); Error = "";
        try
        {
            var rows = await reader.ReadAsync(selected.Workspace, selected.SessionVersion);
            task = rows.SingleOrDefault(r => r.Id == selected.TaskId);
            if (task is null) throw new ApiClientException("This task is unavailable. Return to Tasks and refresh.");
            Parent = rows.FirstOrDefault(r => r.Id == task.Data.Parent?.Id)?.Title ?? "No parent (or parent not cached)";
            foreach (var row in rows.Where(r => r.Data.Parent?.Id == selected.TaskId)) Subtasks.Add(row);
            foreach (var id in task.Data.Prerequisites ?? TaskPrerequisites.Empty)
                BlockedBy.Add(rows.FirstOrDefault(r => r.Id == id) ?? new(id, task.Data with { Title = "Prerequisite not cached — sync and review" }));
            if (task.Conflict) Error = "Resolve this task's conflict in Account tasks before changing its properties.";
        }
        catch (Exception e) when (e is ApiClientException or ArgumentException) { Error = e.Message; }
        catch (Exception) { task = null; Error = "Couldn't read this task. Your saved work is unchanged; return and refresh."; }
        finally { Notify(); }
    }
    public async Task AddSubtaskAsync()
    {
        if (!CanEdit) return;
        var selected = Context;
        try
        {
            _ = reader.Scope(selected.Workspace, selected.SessionVersion);
            if (selected.Workspace == HierarchyWorkspace.Device)
                await device.CreateAsync(new TaskDetails(SubtaskTitle, Parent: new TaskParent(selected.TaskId)));
            else
                await sync.QueueAsync(reader.Scope(selected.Workspace, selected.SessionVersion), Guid.NewGuid(),
                    new SyncTaskData(SubtaskTitle, null, null, TodoApp.Domain.Enums.TaskPriority.Normal, null, false,
                        Parent: new TaskParent(selected.TaskId)));
            SubtaskTitle = "";
            await LoadAsync(selected);
        }
        catch (Exception e) when (e is ArgumentException or ApiClientException) { Error = e.Message; }
    }
    public void WatchSession() => session.Changed += SessionChanged;
    private void SessionChanged()
    {
        if (context?.Workspace != HierarchyWorkspace.SyncedAccount) return;
        task = null; Parent = ""; SubtaskTitle = ""; Subtasks.Clear(); BlockedBy.Clear(); Error = "Account changed. Reopen the task from its account workspace."; Notify();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Title), nameof(Summary), nameof(Description), nameof(Parent), nameof(Classification), nameof(Repeat), nameof(CanEdit) })
            OnPropertyChanged(name);
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
