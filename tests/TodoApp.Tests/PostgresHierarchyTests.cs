using System.Net;
using System.Net.Http.Json;
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
    private async Task<TaskResponse> HierarchyCreate(string title, Guid? parent = null)
    {
        using var response = await client.PostAsJsonAsync("/api/tasks", new { title, tags = new[] { "work" }, parent = new { id = parent } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
    }
    private Task<HttpResponseMessage> Move(Guid id, Guid? parent) => client.PutAsJsonAsync($"/api/tasks/{id}/parent", new { id = parent });

    [PostgresFact]
    public async Task Hierarchy_RestMovesPreserveFieldsLegacyUpdatesPreservePlacementAndDeletionIsRestricted()
    {
        using var anonymous = await Move(Guid.NewGuid(), null); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        As(await Register("hierarchy-rest@example.invalid")); var parent = await HierarchyCreate("Parent"); var child = await HierarchyCreate("Child", parent.Id);
        Assert.Equal(parent.Id, child.ParentTaskId);
        using var blocked = await client.DeleteAsync($"/api/tasks/{parent.Id}"); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var cycle = await Move(parent.Id, child.Id); Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        using var edit = await client.PutAsJsonAsync($"/api/tasks/{child.Id}", new { title = "Legacy edit" });
        Assert.Equal(parent.Id, (await edit.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.ParentTaskId);
        using var detached = await Move(child.Id, null); Assert.Equal(HttpStatusCode.OK, detached.StatusCode);
        var live = (await detached.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        Assert.Null(live.ParentTaskId); Assert.Equal("Legacy edit", live.Title); Assert.Equal(child.Tags, live.Tags); Assert.Equal(child.CreatedAt, live.CreatedAt);
        using var deleted = await client.DeleteAsync($"/api/tasks/{parent.Id}"); Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.NotNull(await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{child.Id}", ClientHarness.JsonOptions));
    }

    [PostgresFact]
    public async Task Hierarchy_ForeignAndMissingParentsHaveSameSafeFailureWithoutPartialMutation()
    {
        var alice = await Register("hierarchy-alice@example.invalid"); var bob = await Register("hierarchy-bob@example.invalid");
        As(alice); var privateParent = await HierarchyCreate("Private"); As(bob); var own = await HierarchyCreate("Bob");
        using var foreignTask = await Move(privateParent.Id, own.Id); Assert.Equal(HttpStatusCode.NotFound, foreignTask.StatusCode);
        using var foreign = await Move(own.Id, privateParent.Id); using var missing = await Move(own.Id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode); Assert.Equal(foreign.StatusCode, missing.StatusCode);
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(foreignProblem!.Detail, (await missing.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Detail);
        using var invalid = await client.PutAsJsonAsync($"/api/tasks/{own.Id}", new { title = "Wrong", categoryName = "Must not exist", parent = new { id = privateParent.Id } });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var empty = await Move(own.Id, Guid.Empty); Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var spoof = await client.PutAsJsonAsync($"/api/tasks/{own.Id}/parent", new { id = (Guid?)null, ownerId = alice.User.Id }); Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        Assert.Equal(own, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{own.Id}", ClientHarness.JsonOptions));
        await using var db = Context(); Assert.Empty(await db.Categories.ToListAsync());
    }

    [PostgresFact]
    public async Task Hierarchy_ConcurrentOppositeMovesCannotCommitACycleAndDepthLimitCountsDescendants()
    {
        As(await Register("hierarchy-concurrency@example.invalid")); var a = await HierarchyCreate("A"); var b = await HierarchyCreate("B");
        var results = await Task.WhenAll(Move(a.Id, b.Id), Move(b.Id, a.Id));
        try { Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in results) response.Dispose(); }
        var root = await HierarchyCreate("Level 1"); var leaf = root;
        for (var level = 2; level <= 16; level++) leaf = await HierarchyCreate($"Level {level}", leaf.Id);
        using var tooDeep = await Move(root.Id, a.Id); Assert.Equal(HttpStatusCode.Conflict, tooDeep.StatusCode);
        using var extra = await client.PostAsJsonAsync("/api/tasks", new { title = "17", parent = new { id = leaf.Id } }); Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
    }

    [PostgresFact]
    public async Task Hierarchy_StructuralConflictIsReceiptedAndExplicitParentRepairKeepsLocalFields()
    {
        As(await Register("hierarchy-conflict@example.invalid"));
        var a = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("A", Parent: new(null))));
        var b = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("B", Parent: new(a.Current.TaskId))));
        var move = new SyncMutation(Guid.NewGuid(), a.Current.TaskId, a.Current.Revision, false, new("Keep local title", CategoryName: "No partial category", Parent: new(b.Current.TaskId)));
        var result = await Applied(move); Assert.True(result.Conflict); Assert.NotNull(result.ConflictReason); Assert.Equal(a.Current, result.Current);
        Assert.Equal(result, await Applied(move));
        await using (var db = Context()) { Assert.Empty(await db.Categories.ToListAsync()); Assert.Equal(3, await db.SyncReceipts.CountAsync()); }
        var repaired = await Applied(move with { OperationId = Guid.NewGuid(), Task = move.Task! with { Parent = new(null) } });
        Assert.False(repaired.Conflict); Assert.Equal("Keep local title", repaired.Current.Task!.Title); Assert.Null(repaired.Current.Task.Parent!.Id);
        var deletion = await Applied(new(Guid.NewGuid(), a.Current.TaskId, repaired.Current.Revision, true, null));
        Assert.True(deletion.Conflict); Assert.NotNull(deletion.ConflictReason); Assert.False(deletion.Current.Deleted);
    }

    [PostgresFact]
    public async Task Hierarchy_MigrationFromTagsRetainsValuesTimesAndAddsNullableRestrictedParent()
    {
        var account = await Register("hierarchy-migration@example.invalid"); var id = Guid.NewGuid();
        await using var db = Context(); await db.GetService<IMigrator>().MigrateAsync("AddTaskTags");
        var raw = DateTimeOffset.UtcNow; var now = new DateTimeOffset(raw.UtcTicks - raw.UtcTicks % 10, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tasks" ("Id","Title","IsCompleted","Priority","CreatedAt","UpdatedAt","OwnerId","Tags")
            VALUES ({id},'Legacy',false,1,{now},{now},{account.User.Id},'["work"]');
            """);
        await db.Database.MigrateAsync(); var restored = await db.Tasks.AsNoTracking().SingleAsync();
        Assert.Null(restored.ParentTaskId); Assert.Equal(TagSet.Parse("work"), restored.Tags); Assert.Equal(now, restored.UpdatedAt); Assert.Equal(now, restored.CreatedAt);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(6, (await db.Database.GetAppliedMigrationsAsync()).Count());
        As(account); var child = await HierarchyCreate("Child", id);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Tasks\" WHERE \"Id\"={id}"));
        Assert.NotEqual(Guid.Empty, child.Id);
    }

    [PostgresFact]
    public async Task Hierarchy_ReplicasOrderCreatesAndDeletesAndConvergeAfterParentMove()
    {
        var account = await Register("hierarchy-replicas@example.invalid");
        var settings = new MemoryClientSettings { ApiBaseUrl = "https://localhost/" }; var session = new ClientSession(settings, new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken, account.TokenType, account.ExpiresAt, new(account.User.Id, account.User.Email)), new Uri(settings.ApiBaseUrl), session.Version);
        var api = new TodoApiClient(client, session); var directory = Path.Combine(Path.GetTempPath(), "todoapp_hierarchy_replicas_" + Guid.NewGuid().ToString("N"));
        var a = new SqliteSyncStore(Path.Combine(directory, "a.db3")); var b = new SqliteSyncStore(Path.Combine(directory, "b.db3"));
        var scope = SqliteSyncStore.Partition(settings.ApiBaseUrl, account.User.Id); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        var engineA = new SyncEngine(session, api, a); var engineB = new SyncEngine(session, api, b);
        try
        {
            // Child enters the dictionary first, then moves under an unsent parent.
            await a.QueueAsync(scope, child, new("Child", Parent: new(null))); await a.QueueAsync(scope, parent, new("Parent", Parent: new(null)));
            var row = (await a.ReadAsync(scope)).Rows[child]; await a.ReparentAsync(scope, child, parent, 0, row.Local!);
            await engineA.RunAsync(); await engineB.RunAsync();
            Assert.Equal(parent, (await b.ReadAsync(scope)).Rows[child].Local!.Parent!.Id);
            Assert.All((await a.ReadAsync(scope)).Rows.Values, r => { Assert.Null(r.Pending); Assert.False(r.Conflict); });
            // Parent's delete is first in this cache's insertion order; child delete must be acknowledged first.
            await b.QueueAsync(scope, child, null); await b.QueueAsync(scope, parent, null);
            await engineB.RunAsync(); await engineA.RunAsync();
            Assert.All((await a.ReadAsync(scope)).Rows.Values, r => { Assert.Null(r.Local); Assert.Null(r.Pending); Assert.False(r.Conflict); });
            await using var db = Context(); Assert.Empty(await db.Tasks.ToListAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
