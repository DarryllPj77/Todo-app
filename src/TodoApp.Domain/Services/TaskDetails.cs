using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Services;

// Input for local create/edit operations; not an HTTP contract or stored entity.
public sealed record TaskDetails(
    string Title,
    string? Description = null,
    DateOnly? DueDate = null,
    TaskPriority Priority = TaskPriority.Normal,
    Category? Category = null);
