using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;
using TodoApp.Domain.Services;

namespace TodoApp.Infrastructure.Services;

// Temporary, process-local storage for Phase 3; SQLite replaces it in Phase 4.
// Intended for sequential local use. Returned entities are live references.
public sealed class InMemoryLocalTaskService : ILocalTaskService
{
    private readonly Dictionary<Guid, TaskItem> tasks = new();
    private readonly TimeProvider timeProvider;

    public InMemoryLocalTaskService(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var result = TaskQueries.ApplyFilter(tasks.Values, filter, today);

        return Task.FromResult(result);
    }

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tasks.TryGetValue(id, out var task);
        return Task.FromResult(task);
    }

    public Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(details);
        var task = new TaskItem(details.Title, details.Description, details.DueDate,
            details.Priority, details.Category);
        tasks.Add(task.Id, task);
        return Task.FromResult(task);
    }

    public Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(details);

        if (!tasks.TryGetValue(id, out var task))
        {
            return Task.FromResult(false);
        }

        task.UpdateDetails(details.Title, details.Description, details.DueDate, details.Priority);
        task.SetCategory(details.Category);
        return Task.FromResult(true);
    }

    public Task<bool> SetCompletedAsync(Guid id, bool isCompleted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!tasks.TryGetValue(id, out var task))
        {
            return Task.FromResult(false);
        }

        task.SetCompleted(isCompleted);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!tasks.Remove(id, out var task))
        {
            return Task.FromResult(false);
        }

        task.SetCategory(null);
        return Task.FromResult(true);
    }
}
