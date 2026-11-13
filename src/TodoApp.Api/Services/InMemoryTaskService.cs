using System.ComponentModel.DataAnnotations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

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
            var id = Guid.NewGuid();
            TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)).Append(new TaskLink(id, null)), id, request.Parent?.Id);
            TaskDependencies.ValidateChange(Nodes().Append(new(id, TaskPrerequisites.Empty, false)), id, request.Prerequisites ?? TaskPrerequisites.Empty, request.IsCompleted);
            RecurrenceRules.ValidateWrite(RepeatState.None, request.Recurrence ?? RepeatState.None, request.DueDate, false, request.IsCompleted);
            var category = ResolveCategory(request.CategoryName);
            var task = TaskItem.CreateWithId(id, request.Title!, request.Description, request.DueDate, request.Priority, category, request.Tags);
            task.UpdateDetails(request.Title!, request.Description, request.DueDate, request.Priority, request.Tags, request.Recurrence);
            if (request.Parent is { } parent) task.SetParent(parent);
            task.SetPrerequisites(request.Prerequisites ?? TaskPrerequisites.Empty);
            if (request.Quadrant is { } quadrant) task.SetQuadrant(quadrant);
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

            var repeat = request.Recurrence ?? task.Recurrence;
            RecurrenceRules.ValidateWrite(task.Recurrence, repeat, request.DueDate, task.IsCompleted, request.IsCompleted);
            if (repeat.IsActive) TaskDependencies.ValidateRepeatActivation(Nodes(), id);
            var prerequisites = request.Prerequisites ?? task.Prerequisites;
            TaskDependencies.ValidateChange(Nodes(), id, prerequisites, (request.IsCompleted && (!task.IsCompleted || !task.Prerequisites.Equals(prerequisites))) || repeat.Occurrence > task.Recurrence.Occurrence);
            if (request.Parent is { } requestedParent) TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, requestedParent.Id);
            var category = ResolveCategory(request.CategoryName);
            task.UpdateDetails(request.Title!, request.Description, request.DueDate, request.Priority, request.Tags, request.Recurrence);
            task.SetCategory(category);
            if (request.Parent is { } parent) task.SetParent(parent);
            task.SetPrerequisites(prerequisites);
            if (request.Quadrant is { } quadrant) task.SetQuadrant(quadrant);
            task.SetCompleted(request.IsCompleted);
            RememberCategory(category);
            return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    public Task<TaskResponse?> SetParentAsync(Guid id, Guid? parentId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskResponse?>(null);
            TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, parentId);
            task.SetParent(new TaskParent(parentId)); return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    private IEnumerable<DependencyNode> Nodes() => tasks.Values.Select(t => new DependencyNode(t.Id, t.Prerequisites, t.IsCompleted, t.Recurrence.IsActive));
    public Task<TaskResponse?> SetPrerequisitesAsync(Guid id, TaskPrerequisites prerequisites, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskResponse?>(null);
            TaskDependencies.ValidateChange(Nodes(), id, prerequisites, task.IsCompleted && !task.Prerequisites.Equals(prerequisites));
            task.SetPrerequisites(prerequisites); return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    public Task<TaskResponse?> SetQuadrantAsync(Guid id, EisenhowerQuadrant quadrant, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskResponse?>(null);
            task.SetQuadrant(quadrant); return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    public Task<TaskResponse?> SetRecurrenceRuleAsync(Guid id, RepeatRule? rule, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskResponse?>(null);
            var next = RecurrenceRules.Configure(task.Recurrence, rule);
            if (next.IsActive) TaskDependencies.ValidateRepeatActivation(Nodes(), id);
            task.SetRecurrence(next); return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }
    public Task<TaskResponse?> CompleteOccurrenceAsync(Guid id, RepeatState expected, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskResponse?>(null);
            var next = RecurrenceRules.Complete(task.Recurrence, expected);
            if (next != task.Recurrence)
            { TaskDependencies.ValidateChange(Nodes(), id, task.Prerequisites, true); task.CompleteOccurrence(expected); }
            return Task.FromResult<TaskResponse?>(TaskResponse.FromEntity(task));
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskDependencies.ValidateDelete(Nodes(), id);
            TaskHierarchy.ValidateDelete(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id);
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
