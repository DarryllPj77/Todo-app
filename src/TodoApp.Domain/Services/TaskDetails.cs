using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Domain.Services;

// Input for local create/edit operations; not an HTTP contract or stored entity.
public sealed record TaskDetails(
    string Title,
    string? Description = null,
    DateOnly? DueDate = null,
    TaskPriority Priority = TaskPriority.Normal,
    Category? Category = null, TagSet? Tags = null, TaskParent? Parent = null, TaskPrerequisites? Prerequisites = null, EisenhowerQuadrant? Quadrant = null, RepeatState? Recurrence = null);
