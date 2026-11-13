using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests
{
    private Task<HttpResponseMessage> Upload(SyncMutation mutation) => client.PostAsJsonAsync("/api/sync", mutation, ClientHarness.JsonOptions);
    private async Task<SyncResult> Applied(SyncMutation mutation)
    {
        using var response = await Upload(mutation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SyncResult>(ClientHarness.JsonOptions))!;
    }
    private async Task<SyncPage> Download(long cursor = 0) =>
        (await client.GetFromJsonAsync<SyncPage>($"/api/sync?cursor={cursor}", ClientHarness.JsonOptions))!;

    [PostgresFact]
    public async Task Sync_ReplaysOperationsAndRejectsReusedIdsWithoutDuplicatingTasks()
    {
        As(await Register("sync-replay@example.invalid"));
        var create = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("One", CategoryName: "Work"));
        var concurrent = await Task.WhenAll(Applied(create), Applied(create));
        Assert.Equal(concurrent[0], concurrent[1]);
        var first = concurrent[0];
        Assert.False(first.Conflict); Assert.Equal(create.TaskId, first.Current.TaskId);
        Assert.Equal(first, await Applied(create));
        using var reused = await Upload(create with { Task = new("Different") });
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        await using var database = Context();
        Assert.Single(await database.Tasks.ToListAsync());
        Assert.Single(await database.SyncReceipts.ToListAsync());
        Assert.Single((await Download()).Changes);
    }
    [PostgresFact]
    public async Task Sync_ConcurrentSameRevisionEditsHaveOneWinnerAndPreserveTheOtherAsConflict()
    {
        As(await Register("sync-race@example.invalid"));
        var first = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Original")));
        var a = new SyncMutation(Guid.NewGuid(), first.Current.TaskId, first.Current.Revision, false, new("A"));
        var b = a with { OperationId = Guid.NewGuid(), Task = new("B") };
        var results = await Task.WhenAll(Applied(a), Applied(b));
        Assert.Single(results, r => !r.Conflict); Assert.Single(results, r => r.Conflict);
        Assert.Equal(results[0].Current, results[1].Current);
    }
    [PostgresFact]
    public async Task Sync_NormalRestWritesAndDeletesAdvanceTheDownloadStream()
    {
        As(await Register("sync-rest@example.invalid"));
        using var created = await client.PostAsJsonAsync("/api/tasks", new { title = "From REST" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = Assert.Single((await Download()).Changes);
        using var changed = await client.PutAsJsonAsync($"/api/tasks/{first.TaskId}", new { title = "Edited REST" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var edited = Assert.Single((await Download(first.Revision)).Changes);
        Assert.Equal("Edited REST", edited.Task!.Title);
        Assert.True(edited.Revision > first.Revision);
        using var deleted = await client.DeleteAsync($"/api/tasks/{first.TaskId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var tombstone = Assert.Single((await Download(edited.Revision)).Changes);
        Assert.True(tombstone.Deleted); Assert.Null(tombstone.Task);
        var staleEdit = await Applied(new(Guid.NewGuid(), first.TaskId, edited.Revision, false, new("Offline edit")));
        Assert.True(staleEdit.Conflict); Assert.Equal(tombstone, staleEdit.Current);
        var resurrect = await Applied(new(Guid.NewGuid(), first.TaskId, tombstone.Revision, false, new("Explicit keep mine")));
        Assert.False(resurrect.Conflict); Assert.Equal(first.TaskId, resurrect.Current.TaskId);
    }
    [PostgresFact]
    public async Task Sync_OwnershipValidationAndCursorErrorsDoNotExposeOtherAccounts()
    {
        using var anonymous = await client.GetAsync("/api/sync"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var alice = await Register("sync-alice@example.invalid"); var bob = await Register("sync-bob@example.invalid");
        As(alice);
        var input = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Private"));
        await Applied(input);
        As(bob); Assert.Empty((await Download()).Changes);
        using var collision = await Upload(input with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        using var invalid = await Upload(new(Guid.Empty, Guid.NewGuid(), -1, false, new(" ")));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var spoof = await client.PostAsJsonAsync("/api/sync", new { operationId = Guid.NewGuid(), taskId = Guid.NewGuid(), baseRevision = 0, deleted = true, ownerId = alice.User.Id });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var invalidCursor = await client.GetAsync("/api/sync?cursor=-1"); Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using var ahead = await client.GetAsync("/api/sync?cursor=1"); Assert.Equal(HttpStatusCode.Conflict, ahead.StatusCode);
        As(alice); Assert.Equal("Private", Assert.Single((await Download()).Changes).Task!.Title);
    }
    [PostgresFact]
    public async Task Sync_DownloadPagesHaveStableIncreasingCursorsAndRetainTombstones()
    {
        As(await Register("sync-pages@example.invalid"));
        for (var i = 0; i < 103; i++)
            await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new($"Task {i}")));
        var first = await Download(); Assert.Equal(100, first.Changes.Length); Assert.True(first.HasMore);
        // Updating a row already in page one moves its latest revision beyond the cursor.
        var old = first.Changes[0];
        await Applied(new(Guid.NewGuid(), old.TaskId, old.Revision, true, null));
        var second = await Download(first.Cursor); Assert.Equal(4, second.Changes.Length); Assert.False(second.HasMore);
        Assert.True(Assert.Single(second.Changes, e => e.TaskId == old.TaskId).Deleted);
        Assert.All(second.Changes, e => Assert.True(e.Revision > first.Cursor));
        Assert.Empty((await Download(second.Cursor)).Changes);
        await using var database = Context(); Assert.False(database.Database.HasPendingModelChanges());
    }
    [PostgresFact]
    public async Task Sync_FailedTaskWriteRollsBackJournalAndReceipt()
    {
        As(await Register("sync-rollback@example.invalid"));
        await using (var database = Context())
            await database.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_sync_task() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Test sync write failure'; END; $$;
                CREATE TRIGGER reject_sync_task BEFORE INSERT ON "Tasks" FOR EACH ROW EXECUTE FUNCTION reject_sync_task();
                """);
        using var response = await Upload(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Rejected", CategoryName: "Rejected")));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var reopened = Context();
        Assert.Empty(await reopened.Tasks.ToListAsync()); Assert.Empty(await reopened.Categories.ToListAsync());
        Assert.Empty(await reopened.SyncRecords.ToListAsync()); Assert.Empty(await reopened.SyncReceipts.ToListAsync());
    }
    [PostgresFact]
    public async Task Sync_FirstDownloadBootstrapsExistingOwnedTasksWithoutClaimingAnonymousRows()
    {
        var account = await Register("sync-bootstrap@example.invalid"); As(account);
        using var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Before sync", categoryName = "Work" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using (var database = Context())
            await database.SyncRecords.ExecuteDeleteAsync(); // Simulate the pre-sync schema's existing owned rows.
        var page = await Download(); var entry = Assert.Single(page.Changes);
        Assert.Equal("Before sync", entry.Task!.Title); Assert.Equal("Work", entry.Task.CategoryName);
        Assert.Equal(page.Cursor, entry.Revision);
        Assert.Empty((await Download(page.Cursor)).Changes);
    }

    [PostgresFact]
    public async Task Sync_TwoDeviceReplicasConvergeAfterOfflineEditsConflictAndDeletion()
    {
        var account = await Register("sync-devices@example.invalid");
        var settings = new MemoryClientSettings { ApiBaseUrl = "https://localhost/" };
        var session = new ClientSession(settings, new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken, account.TokenType, account.ExpiresAt,
            new ApiUser(account.User.Id, account.User.Email)), new Uri(settings.ApiBaseUrl), session.Version);
        var api = new TodoApiClient(client, session);
        var directory = Path.Combine(Path.GetTempPath(), "todoapp_sync_devices_" + Guid.NewGuid().ToString("N"));
        var a = new SqliteSyncStore(Path.Combine(directory, "a.db3")); var b = new SqliteSyncStore(Path.Combine(directory, "b.db3"));
        var partition = SqliteSyncStore.Partition(settings.ApiBaseUrl, account.User.Id);
        var engineA = new SyncEngine(session, api, a); var engineB = new SyncEngine(session, api, b);
        try
        {
            var id = Guid.NewGuid(); await a.QueueAsync(partition, id, new("Created offline"));
            await engineA.RunAsync(); await engineB.RunAsync();
            Assert.Equal("Created offline", (await b.ReadAsync(partition)).Rows[id].Local!.Title);
            await a.QueueAsync(partition, id, new("A offline")); await b.QueueAsync(partition, id, new("B offline"));
            await engineA.RunAsync(); await engineB.RunAsync();
            var conflict = (await b.ReadAsync(partition)).Rows[id];
            Assert.True(conflict.Conflict); Assert.Equal("B offline", conflict.Local!.Title); Assert.Equal("A offline", conflict.Remote!.Task!.Title);
            await b.ResolveAsync(partition, id, true); await engineB.RunAsync(); await engineA.RunAsync();
            Assert.Equal("B offline", (await a.ReadAsync(partition)).Rows[id].Local!.Title);
            await a.QueueAsync(partition, id, null); await engineA.RunAsync(); await engineB.RunAsync();
            Assert.Null((await b.ReadAsync(partition)).Rows[id].Local);
            Assert.True((await b.ReadAsync(partition)).Rows[id].Remote!.Deleted);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
