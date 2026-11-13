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
            for (var sent = 0; sent < 100; sent++)
            {
                session.EnsureVersion(version);
                token.ThrowIfCancellationRequested();
                var state = await store.ReadAsync(partition);
                var row = state.Rows.Values.FirstOrDefault(r => r.Pending is not null && !r.Conflict &&
                    (retryNow || r.RetryAt <= time.GetUtcNow()));
                if (row?.Pending is not { } pending) break;
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
            return $"Sync checked. {pendingCount} queued; {conflicts} conflicts. Queued retries respect backoff; Retry now overrides it.";
        }
        finally { gate.Release(); }
    }
}
