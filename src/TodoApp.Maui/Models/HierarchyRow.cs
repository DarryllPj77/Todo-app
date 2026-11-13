using TodoApp.Domain.Sync;

namespace TodoApp.Maui.Models;

public sealed record ParentChoice(Guid? Id, string Label);
public sealed record HierarchyRow(Guid Id, string Title, Guid? ParentId, int Level, bool Live, bool Conflict,
    SyncTaskData? Local, SyncEntry? Remote, string Status)
{
    public string IndentedTitle => new string(' ', Math.Min(Level - 1, 15) * 2) + Title;
    public string AccessibleTitle => $"Level {Level}. {Title}. {Status}";
    public string ShortTitle => Title + " (" + Id.ToString("N")[..8] + ")";
    public string LocalDetails => Local is null ? "Local: deletion requested" : "Local: " + Describe(Local);
    public string ServerDetails => Remote?.Task is { } task ? "Server: " + Describe(task) : "Server: deleted / not uploaded";
    private static string Describe(SyncTaskData task) => $"{task.Title}\n{task.Description}\n{task.Priority}; {task.DueDate}; {task.CategoryName}; Tags: {task.Tags}; {(task.IsCompleted ? "Completed" : "Pending")}; Parent: {task.Parent?.Id?.ToString() ?? "Root"}";
}
