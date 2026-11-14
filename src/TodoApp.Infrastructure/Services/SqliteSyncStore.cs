using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Sync;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Infrastructure.Services;

public sealed class ReplicaRow
{
    public Guid Id { get; set; }
    public SyncTaskData? Local { get; set; }
    public SyncEntry? Remote { get; set; }
    public SyncMutation? Pending { get; set; }
    public string? ConflictReason { get; set; }
    public bool Conflict { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public string Summary => Conflict ? ConflictReason ?? "Conflict: choose Keep mine or Use server" :
        Pending is not null ? Local is null ? "Deletion queued" : "Changes queued" : "Synced";
    public string Title => Local?.Title ?? Remote?.Task?.Title ?? Pending?.Task?.Title ?? "Deleted task";
}
public sealed class ReplicaState
{
    public long Cursor { get; set; }
    public Dictionary<Guid, ReplicaRow> Rows { get; set; } = new();
    public Dictionary<Guid, string> CaptureReceipts { get; set; } = new();
}

// A separate file/schema from device-only todoapp.db3. Each transaction saves
// replica + immutable outgoing operation + download cursor as one unit.
public sealed class SqliteSyncStore(string path)
{
    public event Func<Task>? Written;
    public static string Partition(string origin, Guid user) => origin + "|" + user.ToString("N");
    public Task<ReplicaState> ReadAsync(string partition) => ExecuteAsync(partition, state => state, false);

    public Task<QuickCaptureResult> CaptureAsync(string partition, Guid captureId, QuickCaptureDraft draft,
        Action authorize) => ExecuteAsync(partition, state =>
    {
        authorize();
        if (captureId == Guid.Empty) throw new ArgumentException("A capture must have an ID.");
        ArgumentNullException.ThrowIfNull(draft);
        if (state.CaptureReceipts.TryGetValue(captureId, out var fingerprint))
        {
            if (fingerprint != draft.Fingerprint) throw new InvalidOperationException("This capture was already used for a different draft.");
            return new QuickCaptureResult(captureId, true);
        }
        if (state.CaptureReceipts.Count >= 10000)
            throw new InvalidOperationException("Quick capture storage is full. Use the normal task editor.");
        if (state.Rows.ContainsKey(captureId)) throw new InvalidOperationException("This task ID already exists.");
        var row = new ReplicaRow { Id = captureId, Local = new SyncTaskData(draft.Title, draft.Description) };
        row.Pending = Mutation(row);
        state.Rows.Add(captureId, row);
        state.CaptureReceipts.Add(captureId, draft.Fingerprint);
        return new QuickCaptureResult(captureId, false);
    }, beforeCommit: authorize);

    public Task QueueAsync(string partition, Guid id, SyncTaskData? data, long? expectedRevision = null, SyncTaskData? expectedLocal = null) => ExecuteAsync(partition, state =>
    {
        if (id == Guid.Empty) throw new ArgumentException("A task must have an ID.");
        Validate(data);
        if (!state.Rows.TryGetValue(id, out var row)) state.Rows.Add(id, row = new ReplicaRow { Id = id });
        if (expectedRevision is { } expected && ((row.Remote?.Revision ?? 0) != expected || row.Local != expectedLocal))
            throw new ArgumentException("This cached task changed while you were editing. Copy your draft, then reload and review it before saving.");
        if (row.Conflict) throw new InvalidOperationException("Resolve this task's conflict before editing it.");
        if (data is null)
        {
            TaskDependencies.ValidateDelete(DependencyNodes(state), id);
            TaskHierarchy.ValidateDelete(Links(state), id);
        }
        else
        {
            data = data with { Parent = data.Parent ?? row.Local?.Parent, Prerequisites = data.Prerequisites ?? row.Local?.Prerequisites, Quadrant = data.Quadrant ?? row.Local?.Quadrant, Recurrence = data.Recurrence ?? row.Local?.Recurrence };
            var currentRepeat = row.Local?.Recurrence ?? new RepeatState(null, row.Remote?.RecurrenceFloor ?? 0, row.Remote?.RecurrenceFloor ?? 0);
            var repeat = data.Recurrence ?? currentRepeat;
            if (data.Recurrence is null && currentRepeat.Occurrence > 0) data = data with { Recurrence = repeat };
            RecurrenceRules.ValidateWrite(currentRepeat, repeat, data.DueDate, row.Local?.IsCompleted ?? false, data.IsCompleted);
            if (repeat.IsActive) TaskDependencies.ValidateRepeatActivation(DependencyNodes(state), id);
            if (repeat.Occurrence > currentRepeat.Occurrence && row.Pending is not null)
                throw new TaskRecurrenceException("Sync the outstanding operation before completing another occurrence.");
            var prerequisites = data.Prerequisites ?? TaskPrerequisites.Empty;
            TaskDependencies.ValidateChange(DependencyNodes(state).Where(t => t.Id != id).Append(new(id, TaskPrerequisites.Empty, false)), id, prerequisites,
                (data.IsCompleted && (row.Local?.IsCompleted != true || !(row.Local.Prerequisites ?? TaskPrerequisites.Empty).Equals(prerequisites))) || repeat.Occurrence > currentRepeat.Occurrence);
            if (data.Parent is { } placement) TaskHierarchy.ValidateMove(Links(state).Where(t => t.Id != id).Append(new TaskLink(id, null)), id, placement.Id);
        }
        row.Local = data;
        // Never replace an outgoing payload: a lost response may already have committed it.
        row.Pending ??= Mutation(row);
        return true;
    });

    private static IEnumerable<TaskLink> Links(ReplicaState state) => state.Rows.Values.Where(r => r.Local is not null)
        .Select(r => new TaskLink(r.Id, r.Local!.Parent?.Id));

    private static IEnumerable<DependencyNode> DependencyNodes(ReplicaState state) => state.Rows.Values.Where(r => r.Local is not null)
        .Select(r => new DependencyNode(r.Id, r.Local!.Prerequisites ?? TaskPrerequisites.Empty, r.Local.IsCompleted, r.Local.Recurrence?.IsActive ?? false));

    public Task SetRecurrenceRuleAsync(string partition, Guid id, RepeatRule? rule, long expectedRevision,
        SyncTaskData expectedLocal, bool keepLocalConflict = false) => ExecuteAsync(partition, state =>
    {
        var row = ExpectedRecurrenceRow(state, id, expectedRevision, expectedLocal);
        if (row.Conflict && !keepLocalConflict) throw new TaskRecurrenceException("Review and confirm the conflict before keeping local fields.");
        var current = row.Conflict
            ? row.Remote?.Task?.Recurrence ?? new RepeatState(null, row.Remote?.RecurrenceFloor ?? 0, row.Remote?.RecurrenceFloor ?? 0)
            : row.Local!.Recurrence ?? RepeatState.None;
        var next = RecurrenceRules.Configure(current, rule);
        var due = next.IsActive ? RecurrenceRules.DueDate(next) : row.Local!.DueDate;
        RecurrenceRules.ValidateWrite(current, next, due, row.Local!.IsCompleted, row.Local.IsCompleted);
        if (next.IsActive) TaskDependencies.ValidateRepeatActivation(DependencyNodes(state), id);
        row.Local = row.Local with { Recurrence = next, DueDate = due };
        if (row.Conflict)
        {
            row.Conflict = false; row.ConflictReason = null; row.Pending = Mutation(row);
            row.Attempts = 0; row.RetryAt = default;
        }
        else row.Pending ??= Mutation(row);
        return true;
    });

    public Task CompleteOccurrenceAsync(string partition, Guid id, long expectedRevision, SyncTaskData expectedLocal) => ExecuteAsync(partition, state =>
    {
        var row = ExpectedRecurrenceRow(state, id, expectedRevision, expectedLocal);
        if (row.Pending is not null || row.Conflict) throw new TaskRecurrenceException("Sync and resolve outstanding changes before completing this occurrence.");
        var current = row.Local!.Recurrence ?? RepeatState.None;
        var next = RecurrenceRules.Complete(current, current);
        TaskDependencies.ValidateChange(DependencyNodes(state), id, row.Local.Prerequisites ?? TaskPrerequisites.Empty, true);
        row.Local = row.Local with { Recurrence = next, DueDate = RecurrenceRules.DueDate(next), IsCompleted = false };
        row.Pending = Mutation(row); return true;
    });

    private static ReplicaRow ExpectedRecurrenceRow(ReplicaState state, Guid id, long revision, SyncTaskData local)
    {
        if (!state.Rows.TryGetValue(id, out var row) || row.Local is null || row.Local != local || (row.Remote?.Revision ?? 0) != revision)
            throw new TaskRecurrenceException("This task changed. Refresh and review recurrence before saving.");
        return row;
    }

    public Task SetQuadrantAsync(string partition, Guid id, EisenhowerQuadrant quadrant, long expectedRevision,
        SyncTaskData expectedLocal, bool keepLocalConflict = false) => ExecuteAsync(partition, state =>
    {
        if (!Enum.IsDefined(quadrant)) throw new ArgumentException("Choose a valid urgency/importance quadrant.");
        if (!state.Rows.TryGetValue(id, out var row) || row.Local is null || row.Local != expectedLocal || (row.Remote?.Revision ?? 0) != expectedRevision)
            throw new ArgumentException("This task changed. Refresh and review its planning before saving.");
        if (row.Conflict && !keepLocalConflict) throw new ArgumentException("Review and confirm the conflict before keeping local fields.");
        row.Local = row.Local with { Quadrant = quadrant };
        if (row.Conflict)
        {
            row.Conflict = false; row.ConflictReason = null; row.Pending = Mutation(row);
            row.Attempts = 0; row.RetryAt = default;
        }
        else row.Pending ??= Mutation(row);
        return true;
    });

    public Task SetPrerequisitesAsync(string partition, Guid id, TaskPrerequisites prerequisites, long expectedRevision,
        SyncTaskData expectedLocal, bool keepLocalConflict = false) => ExecuteAsync(partition, state =>
    {
        if (!state.Rows.TryGetValue(id, out var row) || row.Local is null || row.Local != expectedLocal || (row.Remote?.Revision ?? 0) != expectedRevision)
            throw new TaskDependencyException("This task changed. Refresh and review before changing dependencies.");
        if (row.Conflict && !keepLocalConflict) throw new TaskDependencyException("Review and confirm the conflict before keeping local fields.");
        var previous = row.Local.Prerequisites ?? TaskPrerequisites.Empty;
        TaskDependencies.ValidateChange(DependencyNodes(state), id, prerequisites, row.Local.IsCompleted && !previous.Equals(prerequisites));
        row.Local = row.Local with { Prerequisites = prerequisites };
        if (row.Conflict)
        {
            row.Conflict = false; row.ConflictReason = null; row.Pending = Mutation(row);
            row.Attempts = 0; row.RetryAt = default;
        }
        else row.Pending ??= Mutation(row);
        return true;
    });

    public Task ReparentAsync(string partition, Guid id, Guid? parentId, long expectedRevision,
        SyncTaskData expectedLocal, bool keepLocalConflict = false) => ExecuteAsync(partition, state =>
    {
        var row = state.Rows[id];
        if (row.Local is null || row.Local != expectedLocal || (row.Remote?.Revision ?? 0) != expectedRevision)
            throw new TaskHierarchyException("This task changed. Refresh and review the hierarchy before moving it.");
        if (row.Conflict && !keepLocalConflict) throw new TaskHierarchyException("Review the conflict before choosing Keep local fields with a new parent.");
        TaskDependencies.ValidateChange(DependencyNodes(state), id, row.Local.Prerequisites ?? TaskPrerequisites.Empty,
            row.Local.IsCompleted && row.Remote?.Task?.IsCompleted != true);
        TaskHierarchy.ValidateMove(Links(state), id, parentId);
        row.Local = row.Local with { Parent = new TaskParent(parentId) };
        if (row.Conflict)
        {
            row.Conflict = false; row.ConflictReason = null; row.Pending = Mutation(row);
            row.Attempts = 0; row.RetryAt = default;
        }
        else row.Pending ??= Mutation(row);
        return true;
    });

    public Task ResolveAsync(string partition, Guid id, bool keepMine, long? expectedRevision = null) => ExecuteAsync(partition, state =>
    {
        var row = state.Rows[id];
        if (!row.Conflict) throw new InvalidOperationException("This task has no conflict.");
        if (expectedRevision is { } expected && row.Remote?.Revision != expected)
            throw new ArgumentException("The server version changed. Reload and review both versions before resolving this conflict.");
        row.Conflict = false; row.ConflictReason = null;
        row.Pending = keepMine ? Mutation(row) : null;
        if (!keepMine) row.Local = row.Remote?.Task;
        row.Attempts = 0; row.RetryAt = default;
        return true;
    });

    public Task AcknowledgeAsync(string partition, SyncMutation sent, SyncResult result) => ExecuteAsync(partition, state =>
    {
        var row = state.Rows[sent.TaskId];
        if (row.Pending?.OperationId != sent.OperationId) return false;
        if (result.Current.TaskId != sent.TaskId || result.Current.Revision < sent.BaseRevision || (!result.Conflict && result.Current.Revision == sent.BaseRevision) ||
            result.Current.Deleted != (result.Current.Task is null) || result.Current.RecurrenceFloor is < 0 or > RecurrenceRules.MaxOccurrence ||
            (result.Current.Task is not null && (result.Current.Task.Recurrence?.Occurrence ?? 0) < result.Current.RecurrenceFloor)) throw new InvalidDataException("Invalid sync acknowledgement.");
        Validate(result.Current.Task);
        // A replayed receipt may predate changes already downloaded during backoff.
        // Never move a replica backwards or silently rebase newer local intent.
        var latest = row.Remote is { } known && known.Revision > result.Current.Revision ? known : result.Current;
        row.Remote = latest;
        row.Pending = null;
        row.Attempts = 0; row.RetryAt = default;
        row.ConflictReason = result.ConflictReason;
        if (result.Conflict) row.Conflict = true;
        else if (row.Local == sent.Task) row.Local = latest.Task;
        else if (latest.Revision > result.Current.Revision) row.Conflict = true;
        else row.Pending = Mutation(row); // An edit made while sending becomes the next operation.
        return true;
    });

    public Task FailedAsync(string partition, SyncMutation sent, DateTimeOffset now, bool permanent = false) => ExecuteAsync(partition, state =>
    {
        var row = state.Rows[sent.TaskId];
        if (row.Pending?.OperationId != sent.OperationId) return false;
        row.Attempts = Math.Min(row.Attempts + 1, 10);
        row.RetryAt = permanent ? DateTimeOffset.MaxValue : now.AddSeconds(Math.Min(300, Math.Pow(2, row.Attempts) * 2));
        return true;
    });

    public Task ApplyPageAsync(string partition, long fromCursor, SyncPage page) => ExecuteAsync(partition, state =>
    {
        if (state.Cursor != fromCursor) throw new InvalidOperationException("The download cursor changed; retry sync.");
        if (page.Cursor < fromCursor || page.Changes.Any(e => e.Revision <= fromCursor || e.Revision > page.Cursor ||
            e.TaskId == Guid.Empty || (e.Task?.Quadrant is { } quadrant && !Enum.IsDefined(quadrant)) || e.Task?.Parent?.Id == Guid.Empty || e.Task?.Parent?.Id == e.TaskId || (e.Task?.Prerequisites?.Contains(e.TaskId) ?? false) || e.Deleted != (e.Task is null)) ||
            page.Changes.Select(e => e.Revision).Distinct().Count() != page.Changes.Length ||
            (page.Changes.Length > 0 && page.Changes.Max(e => e.Revision) != page.Cursor) ||
            (page.Changes.Length == 0 && (page.Cursor != fromCursor || page.HasMore)))
            throw new InvalidDataException("Invalid sync download; the cursor was not advanced.");
        foreach (var entry in page.Changes)
        {
            if (entry.RecurrenceFloor is < 0 or > RecurrenceRules.MaxOccurrence || (entry.Task is { } payload && (payload.Recurrence?.Occurrence ?? 0) < entry.RecurrenceFloor))
                throw new InvalidDataException("Invalid recurrence counter; download cursor retained.");
            if (entry.Task?.Recurrence is { } repeat) RecurrenceRules.ValidateWrite(repeat, repeat, entry.Task.DueDate, false, entry.Task.IsCompleted);
            if (!state.Rows.TryGetValue(entry.TaskId, out var row))
                state.Rows.Add(entry.TaskId, row = new ReplicaRow { Id = entry.TaskId });
            if (row.Remote?.Revision > entry.Revision) continue; // Upload ack may be ahead of this page.
            row.Remote = entry;
            if (row.Pending is null && !row.Conflict) row.Local = entry.Task;
        }
        state.Cursor = page.Cursor;
        return true;
    });

    private static SyncMutation Mutation(ReplicaRow row) => new(Guid.NewGuid(), row.Id,
        row.Remote?.Revision ?? 0, row.Local is null, row.Local);

    public static void Validate(SyncTaskData? data)
    {
        if (data is null) return;
        var validated = new TaskItem(data.Title, data.Description, data.DueDate, data.Priority);
        if (data.Quadrant is { } quadrant) validated.SetQuadrant(quadrant);
        if (data.Recurrence is { } repeat) RecurrenceRules.ValidateWrite(repeat, repeat, data.DueDate, false, data.IsCompleted);
        if (data.CategoryName?.Trim().Length > Category.MaxNameLength)
            throw new ArgumentException("Category must be at most 100 characters.");
    }

    private async Task<T> ExecuteAsync<T>(string partition, Func<ReplicaState, T> action, bool write = true, Action? beforeCommit = null)
    {
        var result = await Task.Run(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(partition);
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(command.ExecuteScalar());
            if (version > 7) throw new InvalidDataException("This sync cache needs a newer app. No data was reset.");
            command.CommandText = "CREATE TABLE IF NOT EXISTS ReplicaPartitions (Partition TEXT PRIMARY KEY, State TEXT NOT NULL); PRAGMA user_version=7;";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT State FROM ReplicaPartitions WHERE Partition=$partition;";
            command.Parameters.AddWithValue("$partition", partition);
            var json = command.ExecuteScalar() as string;
            var state = json is null ? new ReplicaState() : JsonSerializer.Deserialize<ReplicaState>(json)
                ?? throw new InvalidDataException("Unreadable sync cache. No data was reset.");
            var operationResult = action(state);
            if (write)
            {
                command.CommandText = "INSERT INTO ReplicaPartitions (Partition, State) VALUES ($partition, $state) ON CONFLICT(Partition) DO UPDATE SET State=$state;";
                command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(state));
                command.ExecuteNonQuery();
            }
            beforeCommit?.Invoke();
            transaction.Commit();
            return operationResult;
        });
        if (write && Written is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList())
                try { await handler(); } catch (Exception) { /* A committed write stays successful; reminder UI provides recovery. */ }
        return result;
    }
}
