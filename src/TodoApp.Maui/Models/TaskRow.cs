using TodoApp.Domain.Entities;

namespace TodoApp.Maui.Models;

public sealed class TaskRow
{
    public Guid Id { get; }
    public string Title { get; }
    public bool IsCompleted { get; }
    public string Summary { get; }
    public string? CategoryName { get; }
    public string Description { get; }
    public string Organization { get; }
    public string Relationships { get; }
    public string Recurrence { get; }
    public string Updated { get; }
    public string SearchableText { get; }

    public TaskRow(TaskItem task)
    {
        Id = task.Id;
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        CategoryName = task.Category?.Name;
        SearchableText = string.Join("\n", task.Title, task.Description, CategoryName, task.Tags.ToString());
        Description = task.Description ?? "No description.";
        Organization = $"Category: {CategoryName ?? "Uncategorized"}\nTags: {(task.Tags.Count == 0 ? "None" : task.Tags.ToString())}";
        Relationships = $"Parent: {task.ParentTaskId?.ToString() ?? "None"}\nPrerequisites: {(task.Prerequisites.Count == 0 ? "None" : string.Join(", ", task.Prerequisites))}";
        Recurrence = TodoApp.Domain.Recurrence.RecurrenceRules.Describe(task.Recurrence);
        Updated = $"Updated {task.UpdatedAt.ToLocalTime():g}";
        var due = task.DueDate is { } date ? $"Due {date:d}" : "No due date";
        Summary = $"{(task.IsCompleted ? "Completed" : "Pending")} · {task.Priority} · {due}";
        Summary += " | " + TodoApp.Domain.Enums.Eisenhower.Label(task.Quadrant);
        if (task.Recurrence.IsActive) Summary += " | " + TodoApp.Domain.Recurrence.RecurrenceRules.Describe(task.Recurrence);
        if (task.ParentTaskId is not null) Summary += " | Subtask";
        if (task.Prerequisites.Count > 0) Summary += $" | Prerequisites: {task.Prerequisites.Count} (see Dependencies)";
        if (task.Tags.Count > 0) Summary += " | Tags: " + task.Tags;
        if (task.Category is not null)
        {
            Summary += $" · {task.Category.Name}";
        }
    }
}
