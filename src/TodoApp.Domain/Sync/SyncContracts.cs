using System.Text.Json.Serialization;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Domain.Sync;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncTaskData(string Title, string? Description = null, DateOnly? DueDate = null,
    TaskPriority Priority = TaskPriority.Normal, string? CategoryName = null, bool IsCompleted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TagSet? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskParent? Parent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskPrerequisites? Prerequisites = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EisenhowerQuadrant? Quadrant = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RepeatState? Recurrence = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncMutation(Guid OperationId, Guid TaskId, long BaseRevision, bool Deleted, SyncTaskData? Task);
public sealed record SyncEntry(Guid TaskId, long Revision, bool Deleted, SyncTaskData? Task,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long RecurrenceFloor = 0);
public sealed record SyncResult(bool Conflict, SyncEntry Current,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConflictReason = null);
public sealed record SyncPage(SyncEntry[] Changes, long Cursor, bool HasMore, int ProtocolVersion = 1);
