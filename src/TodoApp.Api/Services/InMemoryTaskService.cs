using System.ComponentModel.DataAnnotations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;

namespace TodoApp.Api.Services;

// Development-only server data. It never reads the MAUI database or returns live entities.
public sealed class InMemoryTaskService : ITaskService
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, TaskItem> tasks = new();
    private readonly Dictionary<string, Category> categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;

    public InMemoryTaskService(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<TaskResponse>> GetTasksAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            IReadOnlyList<TaskResponse> result = TaskQueries.ApplyFilter(tasks.Values, filter, today)
                .Select(TaskResponse.FromEntity).ToList().AsReadOnly();
            return Task.FromResult(result);
        }
    }

    public Task<TaskResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(tasks.TryGetValue(id, out var task) ? TaskResponse.FromEntity(task) : null);
        }
    }

    public Task<TaskResponse> CreateAsync(TaskWriteRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
            var category = ResolveCategory(request.CategoryName);
            var task = new TaskItem(request.Title!, request.Description, request.DueDate, request.Priority, category);
            task.SetCompleted(request.IsCompleted);
            RememberCategory(category);
            tasks.Add(task.Id, task);
            return Task.FromResult(TaskResponse.FromEntity(task));
        }
    }

    public Task<TaskResponse?> UpdateAsync(Guid id, TaskWriteRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
            if (!tasks.TryGetValue(id, out var task))
            {
                return Task.FromResult<TaskResponse?>(null);
            }

            var category = ResolveCategory(request.CategoryName);
            task.UpdateDetails(request.Title!, request.Description, request.DueDate, request.Priority);
            task.SetCategory(category);
            task.SetCompleted(request.IsCompleted);
            RememberCategory(category);
            return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
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

    private Category? ResolveCategory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalizedName = name.Trim();
        return categories.TryGetValue(normalizedName, out var existing) ? existing : new Category(normalizedName);
    }

    private void RememberCategory(Category? category)
    {
        if (category is not null) categories.TryAdd(category.Name, category);
    }
}
