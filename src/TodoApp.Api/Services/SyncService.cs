using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Services;

public sealed class SyncService(TodoDbContext database, ICurrentUser user, PostgresTaskService tasks, SyncJournal journal)
{
    public async Task<SyncPage?> DownloadAsync(long cursor, CancellationToken token)
    {
        await using var tx = await database.Database.BeginTransactionAsync(token);
        await journal.LockAsync(token);
        var owner = user.UserId;
        var maximum = await database.SyncRecords.Where(r => r.OwnerId == owner).MaxAsync(r => (long?)r.Revision, token) ?? 0;
        if (cursor > maximum) return null; // Restoring an older server backup requires explicit reconciliation.
        var records = await database.SyncRecords.AsNoTracking().Where(r => r.OwnerId == owner && r.Revision > cursor)
            .OrderBy(r => r.Revision).Take(101).ToListAsync(token);
        var changes = records.Take(100).Select(SyncJournal.Entry).ToArray();
        await tx.CommitAsync(token);
        return new SyncPage(changes, changes.LastOrDefault()?.Revision ?? cursor, records.Count > 100);
    }

    public async Task<SyncResult?> ApplyAsync(SyncMutation input, CancellationToken token)
    {
        await using var tx = await database.Database.BeginTransactionAsync(token);
        await journal.LockAsync(token);
        var owner = user.UserId;
        var request = JsonSerializer.Serialize(input);
        var receipt = await database.SyncReceipts.FindAsync([owner, input.OperationId], token);
        if (receipt is not null)
            return receipt.Request == request ? JsonSerializer.Deserialize<SyncResult>(receipt.Response) : null;
        var record = await database.SyncRecords.FindAsync([owner, input.TaskId], token);
        var current = record is null ? new SyncEntry(input.TaskId, 0, true, null) : SyncJournal.Entry(record);
        SyncResult result;
        if (current.Revision != input.BaseRevision)
            result = new SyncResult(true, current);
        else
        {
            // A global task ID belonging to another account is never read or changed.
            if (record is null && await database.Tasks.AnyAsync(t => t.Id == input.TaskId, token)) return null;
            if (input.Deleted)
            {
                if (!await tasks.DeleteAsync(input.TaskId, token))
                    await journal.RecordAsync(input.TaskId, null, token);
            }
            else
            {
                var data = input.Task!;
                var write = new TaskWriteRequest { Title = data.Title, Description = data.Description,
                    DueDate = data.DueDate, Priority = data.Priority, CategoryName = data.CategoryName,
                    IsCompleted = data.IsCompleted };
                if (record is null || record.Deleted) await tasks.CreateWithIdAsync(input.TaskId, write, token);
                else await tasks.UpdateAsync(input.TaskId, write, token);
            }
            record = await database.SyncRecords.FindAsync([owner, input.TaskId], token);
            result = new SyncResult(false, SyncJournal.Entry(record!));
        }
        database.SyncReceipts.Add(new SyncReceipt { OwnerId = owner, OperationId = input.OperationId,
            Request = request, Response = JsonSerializer.Serialize(result) });
        await database.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return result;
    }
}
