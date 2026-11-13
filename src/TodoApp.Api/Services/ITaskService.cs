using TodoApp.Api.Contracts;
using TodoApp.Domain.Enums;

namespace TodoApp.Api.Services;

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> GetTasksAsync(TaskFilter filter, CancellationToken cancellationToken);
    Task<TaskResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<TaskResponse> CreateAsync(TaskWriteRequest request, CancellationToken cancellationToken);
    Task<TaskResponse?> UpdateAsync(Guid id, TaskWriteRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
