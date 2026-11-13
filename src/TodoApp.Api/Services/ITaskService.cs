using TodoApp.Api.Contracts;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Api.Services;

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> GetTasksAsync(TaskFilter filter, CancellationToken cancellationToken);
    Task<TaskResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<TaskResponse> CreateAsync(TaskWriteRequest request, CancellationToken cancellationToken);
    Task<TaskResponse?> UpdateAsync(Guid id, TaskWriteRequest request, CancellationToken cancellationToken);
    Task<TaskResponse?> SetParentAsync(Guid id, Guid? parentId, CancellationToken cancellationToken);
    Task<TaskResponse?> SetPrerequisitesAsync(Guid id, TaskPrerequisites prerequisites, CancellationToken cancellationToken = default);
    Task<TaskResponse?> SetQuadrantAsync(Guid id, EisenhowerQuadrant quadrant, CancellationToken cancellationToken = default);
    Task<TaskResponse?> SetRecurrenceRuleAsync(Guid id, RepeatRule? rule, CancellationToken cancellationToken = default);
    Task<TaskResponse?> CompleteOccurrenceAsync(Guid id, RepeatState expected, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
