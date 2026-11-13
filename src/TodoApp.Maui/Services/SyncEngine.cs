using System.Net;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed class SyncEngine(ClientSession session, TodoApiClient api, SqliteSyncStore store, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<string> RunAsync(bool retryNow = false, CancellationToken token = default)
    {
        if (!await gate.WaitAsync(0, token)) return "Sync is already running in another window.";
        try
        {
            var version = session.Version;
            var saved = await session.RequireAsync(allowUnverified: true);
            if (!session.IsVerified) await api.VerifySessionAsync(token);
            session.EnsureVersion(version);
            var partition = SqliteSyncStore.Partition(saved.Origin, saved.User.Id);
            // Bound one pass; further edits/large downloads continue on the next attempt.
            var checkedProtocol = 1;
            for (var sent = 0; sent < 100; sent++)
            {
                session.EnsureVersion(version);
                token.ThrowIfCancellationRequested();
                var state = await store.ReadAsync(partition);
                var row = state.Rows.Values.FirstOrDefault(r => r.Pending is not null && !r.Conflict &&
                    (retryNow || r.RetryAt <= time.GetUtcNow()) && Eligible(state, r.Pending));
                if (row?.Pending is not { } pending) break;
                // Deletes also need the API's incoming-dependency policy, even
                // though their immutable payload contains no task snapshot.
                var requiredProtocol = pending.Deleted || pending.Task?.Prerequisites is not null ? 4 : pending.Task?.Parent is not null ? 3 : pending.Task?.Tags is not null ? 2 : 1;
                if (requiredProtocol > checkedProtocol)
                {
                    var capability = await api.DownloadAsync(0, token);
                    session.EnsureVersion(version);
                    if (capability.ProtocolVersion < requiredProtocol)
                    {
                        await store.FailedAsync(partition, pending, time.GetUtcNow(), permanent: true);
                        throw new ApiClientException("This API does not support this task feature (tags/hierarchy/dependencies). Upgrade the API, apply its migration, then Retry now. Your queue was retained.");
                    }
                    checkedProtocol = capability.ProtocolVersion;
                }
                try
                {
                    var result = await api.UploadAsync(pending, token);
                    session.EnsureVersion(version);
                    await store.AcknowledgeAsync(partition, pending, result);
                }
                catch (ApiClientException error)
                {
                    await store.FailedAsync(partition, pending, time.GetUtcNow(),
                        error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
                    throw;
                }
            }
            for (var pageNumber = 0; pageNumber < 100; pageNumber++)
            {
                var state = await store.ReadAsync(partition);
                var page = await api.DownloadAsync(state.Cursor, token);
                session.EnsureVersion(version);
                await store.ApplyPageAsync(partition, state.Cursor, page);
                if (!page.HasMore) break;
            }
            var final = await store.ReadAsync(partition);
            session.EnsureVersion(version);
            var pendingCount = final.Rows.Values.Count(r => r.Pending is not null);
            var conflicts = final.Rows.Values.Count(r => r.Conflict);
            return $"Sync checked. {pendingCount} queued; {conflicts} conflicts. Retries respect backoff. Parent/dependency changes may wait for related tasks to sync; Retry now overrides backoff.";
        }
        finally { gate.Release(); }
    }
    private static bool Eligible(ReplicaState state, SyncMutation pending)
    {
        if (pending.Deleted)
            return !state.Rows.Values.Any(r => r.Remote is { Deleted: false, Task: { } task } &&
                (task.Parent?.Id == pending.TaskId || (task.Prerequisites?.Contains(pending.TaskId) ?? false)));
        foreach (var prerequisiteId in pending.Task?.Prerequisites ?? TodoApp.Domain.Relationships.TaskPrerequisites.Empty)
        {
            if (!state.Rows.TryGetValue(prerequisiteId, out var prerequisite)) continue; // Server returns a reviewable conflict.
            if (prerequisite.Remote is not { Deleted: false, Task: not null } && prerequisite.Pending is not null && !prerequisite.Conflict) return false;
            if (pending.Task!.IsCompleted && prerequisite.Remote?.Task?.IsCompleted != true &&
                prerequisite.Pending is not null && prerequisite.Local?.IsCompleted == true && !prerequisite.Conflict) return false;
        }
        if (pending.Task?.Parent?.Id is { } parentId && state.Rows.TryGetValue(parentId, out var parent))
            return parent.Remote is { Deleted: false, Task: not null } || (parent.Pending is null && !parent.Conflict);
        return true;
    }

}
