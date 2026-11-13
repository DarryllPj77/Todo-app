using TodoApp.Domain.Entities;

namespace TodoApp.Maui.Models;

public sealed class TaskRow
{
    public Guid Id { get; }
    public string Title { get; }
    public bool IsCompleted { get; }
    public string Summary { get; }

    public TaskRow(TaskItem task)
    {
        Id = task.Id;
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        var due = task.DueDate is { } date ? $"Due {date:d}" : "No due date";
        Summary = $"{(task.IsCompleted ? "Completed" : "Pending")} · {task.Priority} · {due}";
        if (task.Tags.Count > 0) Summary += " | Tags: " + task.Tags;
        if (task.Category is not null)
        {
            Summary += $" · {task.Category.Name}";
        }
    }
}
