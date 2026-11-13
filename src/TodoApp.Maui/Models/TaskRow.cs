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
        Summary += " | " + TodoApp.Domain.Enums.Eisenhower.Label(task.Quadrant);
        if (task.ParentTaskId is not null) Summary += " | Subtask";
        if (task.Prerequisites.Count > 0) Summary += $" | Prerequisites: {task.Prerequisites.Count} (see Dependencies)";
        if (task.Tags.Count > 0) Summary += " | Tags: " + task.Tags;
        if (task.Category is not null)
        {
            Summary += $" · {task.Category.Name}";
        }
    }
}
