using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;

namespace TodoApp.Api.Contracts;

public sealed record TaskResponse(Guid Id, string Title, string? Description, bool IsCompleted,
    DateOnly? DueDate, TaskPriority Priority, Guid? CategoryId, string? CategoryName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static TaskResponse FromEntity(TaskItem task) => new(task.Id, task.Title, task.Description,
        task.IsCompleted, task.DueDate, task.Priority, task.CategoryId, task.Category?.Name,
        task.CreatedAt, task.UpdatedAt);
}
