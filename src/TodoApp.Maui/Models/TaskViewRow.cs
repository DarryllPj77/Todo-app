using TodoApp.Domain.Views;

namespace TodoApp.Maui.Models;

public sealed record TaskViewRow(TaskViewCandidate Task, string SyncStatus = "Device only")
{
    public Guid Id => Task.Id;
    public string Title => Task.Title;
    public string Description => Task.Description ?? "No description";
    public string Summary => $"{(Task.Completed ? "Completed" : "Pending")} | {Task.Priority} | " +
        (Task.DueDate is { } date ? $"Due {date:d}" : "No due date") + " | " + (Task.CategoryName ?? "Uncategorized") + " | " + SyncStatus + (Task.Tags is { Count: > 0 } ? " | Tags: " + Task.Tags : "");
}
