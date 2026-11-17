using TodoApp.Domain.Sync;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Models;

public sealed record TaskContext(HierarchyWorkspace Workspace, Guid TaskId, int SessionVersion);
public sealed record WorkspaceTask(Guid Id, SyncTaskData Data, bool Conflict = false)
{
    public string Title => Data.Title;
    public string Summary => $"{(Data.IsCompleted ? "Completed" : "Pending")} · {Data.Priority} · " +
        (Data.DueDate is { } due ? $"Due {due:d}" : "No due date");
}
