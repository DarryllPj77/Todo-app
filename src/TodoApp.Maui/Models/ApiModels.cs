using TodoApp.Domain.Enums;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Relationships;
using System.Text.Json.Serialization;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Maui.Models;

public sealed record ApiUser(Guid Id, string Email);
public sealed record ApiAuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, ApiUser User);
public sealed record SavedSession(string Origin, string AccessToken, DateTimeOffset ExpiresAt, ApiUser User);
public sealed record ApiTaskWrite(string Title, string? Description = null, DateOnly? DueDate = null,
    TaskPriority Priority = TaskPriority.Normal, string? CategoryName = null, bool IsCompleted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TagSet? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskParent? Parent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskPrerequisites? Prerequisites = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EisenhowerQuadrant? Quadrant = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RepeatState? Recurrence = null);
public sealed record ApiTask(Guid Id, string Title, string? Description, bool IsCompleted, DateOnly? DueDate,
    TaskPriority Priority, Guid? CategoryId, string? CategoryName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, TagSet? Tags = null, Guid? ParentTaskId = null, TaskPrerequisites? Prerequisites = null, EisenhowerQuadrant Quadrant = EisenhowerQuadrant.Unclassified, RepeatState? Recurrence = null)
{
    public string Summary => $"{(IsCompleted ? "Completed" : "Pending")} · {Priority} · " +
        (DueDate is { } date ? $"Due {date:d}" : "No due date") +
        (string.IsNullOrWhiteSpace(CategoryName) ? "" : $" · {CategoryName}") +
        (Prerequisites is { Count: > 0 } ? $" | Prerequisites: {Prerequisites.Count}" : "") + " | " + Eisenhower.Label(Quadrant) + (Recurrence?.IsActive == true ? " | " + RecurrenceRules.Describe(Recurrence) : "");
}
