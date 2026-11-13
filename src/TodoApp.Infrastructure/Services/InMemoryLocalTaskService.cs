using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;

using TodoApp.Domain.Recurrence;

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
        if (details.Quadrant is { } quadrant && !Enum.IsDefined(quadrant)) throw new ArgumentException("Choose a valid urgency/importance quadrant.");
        RecurrenceRules.ValidateWrite(RepeatState.None, details.Recurrence ?? RepeatState.None, details.DueDate, false, false);
        var id = Guid.NewGuid();
        TaskDependencies.ValidateChange(Nodes().Append(new(id, TaskPrerequisites.Empty, false)), id, details.Prerequisites ?? TaskPrerequisites.Empty);
        TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)).Append(new TaskLink(id, null)), id, details.Parent?.Id);
        var task = TaskItem.CreateWithId(id, details.Title, details.Description, details.DueDate, details.Priority, details.Category, details.Tags);
        task.SetParent(details.Parent ?? new TaskParent(null));
        task.SetPrerequisites(details.Prerequisites ?? TaskPrerequisites.Empty);
        task.SetQuadrant(details.Quadrant ?? EisenhowerQuadrant.Unclassified);
        task.UpdateDetails(details.Title, details.Description, details.DueDate, details.Priority, details.Tags, details.Recurrence);
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

        if (details.Quadrant is { } quadrant && !Enum.IsDefined(quadrant)) throw new ArgumentException("Choose a valid urgency/importance quadrant.");
        var recurrence = details.Recurrence ?? task.Recurrence;
        RecurrenceRules.ValidateWrite(task.Recurrence, recurrence, details.DueDate, task.IsCompleted, task.IsCompleted);
        if (recurrence.IsActive) TaskDependencies.ValidateRepeatActivation(Nodes(), id);
        if (recurrence.Occurrence > task.Recurrence.Occurrence) TaskDependencies.ValidateChange(Nodes(), id, details.Prerequisites ?? task.Prerequisites, true);
        if (details.Prerequisites is { } prerequisites) TaskDependencies.ValidateChange(Nodes(), id, prerequisites, task.IsCompleted && !task.Prerequisites.Equals(prerequisites));
        if (details.Parent is { } parent) TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, parent.Id);
        task.UpdateDetails(details.Title, details.Description, details.DueDate, details.Priority, details.Tags, details.Recurrence);
        if (details.Parent is { } change) task.SetParent(change);
        if (details.Prerequisites is { } changed) task.SetPrerequisites(changed);
        if (details.Quadrant is { } changeQuadrant) task.SetQuadrant(changeQuadrant);
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

        if (isCompleted && !task.IsCompleted) TaskDependencies.ValidateChange(Nodes(), id, task.Prerequisites, true);
        task.SetCompleted(isCompleted);
        return Task.FromResult(true);
    }

    public Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tasks.TryGetValue(id, out var task)) return Task.FromResult(false);
        TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, parentId);
        task.SetParent(new TaskParent(parentId)); return Task.FromResult(true);
    }

    private IEnumerable<DependencyNode> Nodes() => tasks.Values.Select(t => new DependencyNode(t.Id, t.Prerequisites, t.IsCompleted, t.Recurrence.IsActive));
    public Task<bool> SetPrerequisitesAsync(Guid id, TaskPrerequisites prerequisites, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tasks.TryGetValue(id, out var task)) return Task.FromResult(false);
        TaskDependencies.ValidateChange(Nodes(), id, prerequisites, task.IsCompleted && !task.Prerequisites.Equals(prerequisites));
        task.SetPrerequisites(prerequisites); return Task.FromResult(true);
    }

    public Task<bool> SetQuadrantAsync(Guid id, EisenhowerQuadrant quadrant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tasks.TryGetValue(id, out var task)) return Task.FromResult(false);
        task.SetQuadrant(quadrant); return Task.FromResult(true);
    }

    public Task<TaskItem?> SetRecurrenceRuleAsync(Guid id, RepeatRule? rule, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskItem?>(null);
        var next = RecurrenceRules.Configure(task.Recurrence, rule);
        if (next.IsActive) TaskDependencies.ValidateRepeatActivation(Nodes(), id);
        task.SetRecurrence(next); return Task.FromResult<TaskItem?>(task);
    }
    public Task<TaskItem?> CompleteOccurrenceAsync(Guid id, RepeatState expected, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tasks.TryGetValue(id, out var task)) return Task.FromResult<TaskItem?>(null);
        var next = RecurrenceRules.Complete(task.Recurrence, expected);
        if (next != task.Recurrence)
        {
            TaskDependencies.ValidateChange(Nodes(), id, task.Prerequisites, true);
            task.CompleteOccurrence(expected);
        }
        return Task.FromResult<TaskItem?>(task);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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
