using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Sync;
using TodoApp.Domain.Relationships;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Services;

public sealed class SyncJournal(TodoDbContext database, ICurrentUser user)
{
    // Every normal REST writer and sync writer takes this transaction-scoped lock
    // before touching tasks. Per-owner ordering makes download cursors commit-safe.
    public async Task LockAsync(CancellationToken token)
    {
        var owner = user.UserId;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({owner.ToString()}, 0));", token);
        var known = await database.SyncRecords.Where(r => r.OwnerId == owner).Select(r => r.TaskId).ToListAsync(token);
        var legacy = await database.Tasks.Include(t => t.Category)
            .Where(t => EF.Property<Guid?>(t, "OwnerId") == owner && !known.Contains(t.Id)).ToListAsync(token);
        foreach (var task in legacy)
            await RecordAsync(task.Id, new SyncTaskData(task.Title, task.Description, task.DueDate,
                task.Priority, task.Category?.Name, task.IsCompleted, task.Tags, new TaskParent(task.ParentTaskId)), token);
    }

    public async Task<SyncEntry> RecordAsync(Guid id, SyncTaskData? data, CancellationToken token)
    {
        var owner = user.UserId;
        var revision = (await database.SyncRecords.Where(r => r.OwnerId == owner)
            .MaxAsync(r => (long?)r.Revision, token) ?? 0) + 1;
        var record = await database.SyncRecords.FindAsync([owner, id], token);
        if (record is null)
        {
            record = new SyncRecord { OwnerId = owner, TaskId = id };
            database.SyncRecords.Add(record);
        }
        record.Revision = revision;
        record.Deleted = data is null;
        record.Payload = data is null ? null : JsonSerializer.Serialize(data);
        await database.SaveChangesAsync(token);
        return Entry(record);
    }

    public static SyncEntry Entry(SyncRecord record) => new(record.TaskId, record.Revision,
        record.Deleted, record.Payload is null ? null : JsonSerializer.Deserialize<SyncTaskData>(record.Payload));
}
