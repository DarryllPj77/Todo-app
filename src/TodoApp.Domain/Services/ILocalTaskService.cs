using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Services;

public interface ILocalTaskService
{
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All,
        CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken cancellationToken = default);
    Task<bool> SetCompletedAsync(Guid id, bool isCompleted, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
