using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;

namespace TodoApp.Domain.Services;

public interface ILocalTaskService
{
    // Treat returned entities as read models. Save changes through this service;
    // persistent implementations return detached snapshots, not tracked objects.
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All,
        CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken cancellationToken = default);
    Task<bool> SetCompletedAsync(Guid id, bool isCompleted, CancellationToken cancellationToken = default);
    Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default);
    Task<bool> SetPrerequisitesAsync(Guid id, TaskPrerequisites prerequisites, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
