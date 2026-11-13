using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests
{
    private static TaskPrerequisites Prerequisites(params Guid[] ids) => TaskPrerequisites.FromIds(ids);
    private Task<HttpResponseMessage> Dependencies(Guid id, params Guid[] ids) => client.PutAsJsonAsync($"/api/tasks/{id}/prerequisites", Prerequisites(ids));

    [PostgresFact]
    public async Task Dependencies_RestBlocksCompletionPreservesOmittedLinksAndRestrictsDeletion()
    {
        using var anonymous = await Dependencies(Guid.NewGuid()); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        As(await Register("dependencies-rest@example.invalid")); var prerequisite = await HierarchyCreate("First"); var consumer = await HierarchyCreate("Second", prerequisite.Id);
        using var linked = await Dependencies(consumer.Id, prerequisite.Id); Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var linkedTask = (await linked.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        Assert.Equal(Prerequisites(prerequisite.Id), linkedTask.Prerequisites); Assert.Equal(consumer.ParentTaskId, linkedTask.ParentTaskId); Assert.Equal(consumer.Tags, linkedTask.Tags);
        using var blocked = await client.PutAsJsonAsync($"/api/tasks/{consumer.Id}", new { title = "Wrong", isCompleted = true, categoryName = "No partial category" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(linkedTask, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{consumer.Id}", ClientHarness.JsonOptions));
        using var deletion = await client.DeleteAsync($"/api/tasks/{prerequisite.Id}"); Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        using var legacyEdit = await client.PutAsJsonAsync($"/api/tasks/{consumer.Id}", new { title = "Legacy edit" });
        Assert.Equal(Prerequisites(prerequisite.Id), (await legacyEdit.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Prerequisites);
        using var finishFirst = await client.PutAsJsonAsync($"/api/tasks/{prerequisite.Id}", new { title = "First", isCompleted = true }); Assert.Equal(HttpStatusCode.OK, finishFirst.StatusCode);
        using var finishSecond = await client.PutAsJsonAsync($"/api/tasks/{consumer.Id}", new { title = "Second", isCompleted = true }); Assert.Equal(HttpStatusCode.OK, finishSecond.StatusCode);
        using var reopenFirst = await client.PutAsJsonAsync($"/api/tasks/{prerequisite.Id}", new { title = "First", isCompleted = false }); Assert.Equal(HttpStatusCode.OK, reopenFirst.StatusCode);
        Assert.True((await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{consumer.Id}", ClientHarness.JsonOptions))!.IsCompleted);
        using var clear = await Dependencies(consumer.Id); Assert.Empty((await clear.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Prerequisites!);
        using var detach = await Move(consumer.Id, null); Assert.Equal(HttpStatusCode.OK, detach.StatusCode);
        using var removed = await client.DeleteAsync($"/api/tasks/{prerequisite.Id}"); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await using var db = Context(); Assert.Empty(await db.Categories.ToListAsync());
    }

    [PostgresFact]
    public async Task Dependencies_RejectForeignMissingMalformedAndSpoofedLinksWithoutPartialWrites()
    {
        var alice = await Register("dependencies-alice@example.invalid"); var bob = await Register("dependencies-bob@example.invalid");
        As(alice); var secret = await HierarchyCreate("Private"); As(bob); var own = await HierarchyCreate("Own");
        using var hidden = await Dependencies(secret.Id, own.Id); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var foreign = await Dependencies(own.Id, secret.Id); using var missing = await Dependencies(own.Id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode); Assert.Equal(foreign.StatusCode, missing.StatusCode);
        Assert.Equal((await foreign.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Detail,
            (await missing.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Detail);
        var invalid = new[] { "[null]", "[1]", "[\"bad\"]", "null", "{\"ownerId\":\"" + alice.User.Id + "\"}", JsonSerializer.Serialize(Enumerable.Range(0, 33).Select(_ => Guid.NewGuid())) };
        foreach (var json in invalid)
        {
            using var response = await client.PutAsync($"/api/tasks/{own.Id}/prerequisites", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var spoof = await client.PutAsJsonAsync($"/api/tasks/{own.Id}", new { title = "Wrong", prerequisites = new[] { secret.Id }, ownerId = bob.User.Id });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        Assert.Equal(own, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{own.Id}", ClientHarness.JsonOptions));
    }

    [PostgresFact]
    public async Task Dependencies_ConcurrentOppositeEdgesHaveOneWinnerAndCannotCreateACycle()
    {
        As(await Register("dependencies-race@example.invalid")); var a = await HierarchyCreate("A"); var b = await HierarchyCreate("B");
        var results = await Task.WhenAll(Dependencies(a.Id, b.Id), Dependencies(b.Id, a.Id));
        try { Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in results) response.Dispose(); }
        var all = (await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", ClientHarness.JsonOptions))!;
        Assert.Equal(1, all.Sum(t => t.Prerequisites!.Count));
    }

    [PostgresFact]
    public async Task Dependencies_MigrationRetainsHierarchyTagsOwnersAndTimestamps()
    {
        var account = await Register("dependencies-migration@example.invalid"); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        await using var db = Context(); await db.GetService<IMigrator>().MigrateAsync("AddTaskHierarchy");
        var raw = DateTimeOffset.UtcNow; var now = new DateTimeOffset(raw.UtcTicks - raw.UtcTicks % 10, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tasks" ("Id","Title","IsCompleted","Priority","CreatedAt","UpdatedAt","OwnerId","Tags","ParentTaskId")
            VALUES ({parent},'Parent',false,1,{now},{now},{account.User.Id},'[]',NULL),
                ({child},'Child',true,2,{now},{now},{account.User.Id},'["work"]',{parent});
            """);
        await db.Database.MigrateAsync(); var restored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == child);
        Assert.Empty(restored.Prerequisites); Assert.Equal(parent, restored.ParentTaskId); Assert.Equal(TagSet.Parse("work"), restored.Tags);
        Assert.Equal(now, restored.UpdatedAt); Assert.Equal(now, restored.CreatedAt); Assert.True(restored.IsCompleted);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
        As(account); Assert.Equal(2, (await Download()).Changes.Length); Assert.Equal(6, (await Download()).ProtocolVersion);
    }

    [PostgresFact]
    public async Task Dependencies_StructuralConflictsReplayAndRepairWithoutLosingLocalFields()
    {
        As(await Register("dependencies-sync-conflict@example.invalid"));
        var a = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("A", Prerequisites: Prerequisites())));
        var b = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("B", Prerequisites: Prerequisites(a.Current.TaskId))));
        var mutation = new SyncMutation(Guid.NewGuid(), a.Current.TaskId, a.Current.Revision, false,
            new("Preserved local title", CategoryName: "No partial category", Prerequisites: Prerequisites(b.Current.TaskId)));
        var rejected = await Applied(mutation); Assert.True(rejected.Conflict); Assert.NotNull(rejected.ConflictReason); Assert.Equal(a.Current, rejected.Current);
        Assert.Equal(rejected, await Applied(mutation));
        await using (var db = Context()) { Assert.Empty(await db.Categories.ToListAsync()); Assert.Equal(3, await db.SyncReceipts.CountAsync()); }
        var repaired = await Applied(mutation with { OperationId = Guid.NewGuid(), Task = mutation.Task! with { Prerequisites = Prerequisites() } });
        Assert.False(repaired.Conflict); Assert.Equal("Preserved local title", repaired.Current.Task!.Title); Assert.Empty(repaired.Current.Task.Prerequisites!);
        var blocked = await Applied(new(Guid.NewGuid(), b.Current.TaskId, b.Current.Revision, false, b.Current.Task! with { IsCompleted = true }));
        Assert.True(blocked.Conflict); Assert.NotNull(blocked.ConflictReason);
        var deleted = await Applied(new(Guid.NewGuid(), a.Current.TaskId, repaired.Current.Revision, true, null)); Assert.True(deleted.Conflict); Assert.False(deleted.Current.Deleted);
    }

    [PostgresFact]
    public async Task Dependencies_LegacyHierarchyReceiptReplayDoesNotClobberNewLinks()
    {
        As(await Register("dependencies-receipt@example.invalid"));
        var legacy = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Legacy", Tags: TagSet.Parse("work"), Parent: new(null)));
        Assert.DoesNotContain("Prerequisites", JsonSerializer.Serialize(legacy));
        var original = await Applied(legacy);
        var oldResult = original with { Current = original.Current with { Task = original.Current.Task! with { Prerequisites = null } } };
        await using (var db = Context()) { var receipt = await db.SyncReceipts.SingleAsync(); receipt.Response = JsonSerializer.Serialize(oldResult); await db.SaveChangesAsync(); }
        var prerequisite = await HierarchyCreate("First"); using var linked = await Dependencies(legacy.TaskId, prerequisite.Id); Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(oldResult, await Applied(legacy));
        Assert.Equal(Prerequisites(prerequisite.Id), (await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{legacy.TaskId}", ClientHarness.JsonOptions))!.Prerequisites);
    }

    [PostgresFact]
    public async Task Dependencies_TwoReplicasOrderCreatesCompletionAndLinkRemovalBeforeDeletion()
    {
        var account = await Register("dependencies-replicas@example.invalid"); var settings = new MemoryClientSettings { ApiBaseUrl = "https://localhost/" };
        var session = new ClientSession(settings, new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken, account.TokenType, account.ExpiresAt, new(account.User.Id, account.User.Email)), new Uri(settings.ApiBaseUrl), session.Version);
        var api = new TodoApiClient(client, session); var directory = Path.Combine(Path.GetTempPath(), "todoapp_dependency_replicas_" + Guid.NewGuid().ToString("N"));
        var a = new SqliteSyncStore(Path.Combine(directory, "a.db3")); var b = new SqliteSyncStore(Path.Combine(directory, "b.db3"));
        var scope = SqliteSyncStore.Partition(settings.ApiBaseUrl, account.User.Id); var prerequisite = Guid.NewGuid(); var consumer = Guid.NewGuid();
        var engineA = new SyncEngine(session, api, a); var engineB = new SyncEngine(session, api, b);
        try
        {
            // Consumer enters first. Its frozen create stays empty; the next
            // operation must wait for the unsent prerequisite to exist.
            await a.QueueAsync(scope, consumer, new("Second", Prerequisites: Prerequisites()));
            await a.QueueAsync(scope, prerequisite, new("First", Prerequisites: Prerequisites()));
            var consumerRow = (await a.ReadAsync(scope)).Rows[consumer];
            await a.SetPrerequisitesAsync(scope, consumer, Prerequisites(prerequisite), 0, consumerRow.Local!);
            await engineA.RunAsync(); await engineB.RunAsync(); Assert.Equal(Prerequisites(prerequisite), (await b.ReadAsync(scope)).Rows[consumer].Local!.Prerequisites);
            // Consumer remains first in A: its completion must wait for First.
            await a.QueueAsync(scope, prerequisite, new("First", IsCompleted: true)); await a.QueueAsync(scope, consumer, new("Second", IsCompleted: true));
            await engineA.RunAsync(); await engineB.RunAsync();
            Assert.All((await a.ReadAsync(scope)).Rows.Values, r => { Assert.True(r.Local!.IsCompleted); Assert.Null(r.Pending); Assert.False(r.Conflict); });
            // Download order places First before Second in B: deletion must
            // wait until Second's incoming link is removed on the server.
            await b.QueueAsync(scope, consumer, new("Second", IsCompleted: true, Prerequisites: Prerequisites()));
            await b.QueueAsync(scope, prerequisite, null);
            await engineB.RunAsync(); await engineA.RunAsync(); var final = await a.ReadAsync(scope);
            Assert.Null(final.Rows[prerequisite].Local); Assert.Empty(final.Rows[consumer].Local!.Prerequisites!);
            Assert.All(final.Rows.Values, r => { Assert.False(r.Conflict); Assert.Null(r.Pending); });
            await using var db = Context(); Assert.Single(await db.Tasks.ToListAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
