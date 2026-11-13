using System.Text.Json.Serialization;
using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Sync;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncTaskData(string Title, string? Description = null, DateOnly? DueDate = null,
    TaskPriority Priority = TaskPriority.Normal, string? CategoryName = null, bool IsCompleted = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncMutation(Guid OperationId, Guid TaskId, long BaseRevision, bool Deleted, SyncTaskData? Task);
public sealed record SyncEntry(Guid TaskId, long Revision, bool Deleted, SyncTaskData? Task);
public sealed record SyncResult(bool Conflict, SyncEntry Current);
public sealed record SyncPage(SyncEntry[] Changes, long Cursor, bool HasMore);
