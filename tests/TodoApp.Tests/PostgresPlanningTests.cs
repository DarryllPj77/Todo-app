using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests
{
    private Task<HttpResponseMessage> Plan(Guid id, EisenhowerQuadrant quadrant) => client.PutAsJsonAsync($"/api/tasks/{id}/quadrant", quadrant);
    [PostgresFact]
    public async Task Planning_RestChangesOnlyClassificationAndLegacyUpdatesPreserveIt()
    {
        using var anonymous = await Plan(Guid.NewGuid(), EisenhowerQuadrant.Neither); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        As(await Register("planning-rest@example.invalid")); var parent = await HierarchyCreate("First"); var child = await HierarchyCreate("Second", parent.Id);
        using var linked = await Dependencies(child.Id, parent.Id); Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using var changed = await Plan(child.Id, EisenhowerQuadrant.ImportantNotUrgent); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var planned = (await changed.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, planned.Quadrant); Assert.Equal(child.Title, planned.Title); Assert.Equal(child.ParentTaskId, planned.ParentTaskId);
        Assert.Equal(child.Tags, planned.Tags); Assert.Equal(Prerequisites(parent.Id), planned.Prerequisites); Assert.Equal(child.CreatedAt, planned.CreatedAt);
        using var legacy = await client.PutAsJsonAsync($"/api/tasks/{child.Id}", new { title = "Legacy edit" });
        Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, (await legacy.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Quadrant);
        using var nullUpdate = await client.PutAsJsonAsync($"/api/tasks/{child.Id}", new { title = "Null preserves", quadrant = (string?)null }); Assert.Equal(HttpStatusCode.OK, nullUpdate.StatusCode);
        Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, (await nullUpdate.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Quadrant);
        using var cleared = await Plan(child.Id, EisenhowerQuadrant.Unclassified); Assert.Equal(EisenhowerQuadrant.Unclassified, (await cleared.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Quadrant);
        var snapshot = (await Download()).Changes.Single(e => e.TaskId == child.Id); Assert.Equal(EisenhowerQuadrant.Unclassified, snapshot.Task!.Quadrant);
    }
    [PostgresFact]
    public async Task Planning_InvalidWireValuesAndForeignTasksCannotMutateTheTask()
    {
        var alice = await Register("planning-alice@example.invalid"); var bob = await Register("planning-bob@example.invalid");
        As(alice); var task = await HierarchyCreate("Private"); As(bob);
        using var foreign = await Plan(task.Id, EisenhowerQuadrant.UrgentImportant); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        As(alice);
        foreach (var body in new[] { "0", "99", "\"0\"", "\"future\"", "null", "{}", "{\"quadrant\":\"Neither\",\"ownerId\":\"" + bob.User.Id + "\"}" })
        {
            using var invalid = await client.PutAsync($"/api/tasks/{task.Id}/quadrant", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var invalidFull = await client.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Wrong", quadrant = 1 }); Assert.Equal(HttpStatusCode.BadRequest, invalidFull.StatusCode);
        Assert.Equal(task, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}", ClientHarness.JsonOptions));
    }
    [PostgresFact]
    public async Task Planning_MigrationKeepsDependenciesHierarchyTagsOwnersAndTimestamps()
    {
        var account = await Register("planning-migration@example.invalid"); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        await using var db = Context(); await db.GetService<IMigrator>().MigrateAsync("AddTaskDependencies");
        var raw = DateTimeOffset.UtcNow; var now = new DateTimeOffset(raw.UtcTicks - raw.UtcTicks % 10, TimeSpan.Zero); var prerequisites = JsonSerializer.Serialize(Prerequisites(parent));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tasks" ("Id","Title","IsCompleted","Priority","CreatedAt","UpdatedAt","OwnerId","Tags","ParentTaskId","Prerequisites")
            VALUES ({parent},'Parent',false,1,{now},{now},{account.User.Id},'[]',NULL,'[]'),
                ({child},'Child',false,2,{now},{now},{account.User.Id},'["work"]',{parent},{prerequisites});
            """);
        await db.Database.MigrateAsync(); var restored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == child);
        Assert.Equal(EisenhowerQuadrant.Unclassified, restored.Quadrant); Assert.Equal(Prerequisites(parent), restored.Prerequisites); Assert.Equal(parent, restored.ParentTaskId);
        Assert.Equal(TagSet.Parse("work"), restored.Tags); Assert.Equal(now, restored.UpdatedAt); Assert.Equal(now, restored.CreatedAt);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Tasks\" SET \"Quadrant\"=99 WHERE \"Id\"={child}"));
        As(account); Assert.Equal(EisenhowerQuadrant.Unclassified, (await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{child}", ClientHarness.JsonOptions))!.Quadrant);
    }
    [PostgresFact]
    public async Task Planning_ConcurrentRevisionEditsHaveOneWinnerAndReplayTheConflict()
    {
        As(await Register("planning-race@example.invalid"));
        var original = await Applied(new(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Task", Quadrant: EisenhowerQuadrant.Unclassified)));
        var a = new SyncMutation(Guid.NewGuid(), original.Current.TaskId, original.Current.Revision, false, new("Task", Quadrant: EisenhowerQuadrant.UrgentImportant));
        var b = a with { OperationId = Guid.NewGuid(), Task = a.Task! with { Quadrant = EisenhowerQuadrant.ImportantNotUrgent } };
        var results = await Task.WhenAll(Applied(a), Applied(b)); Assert.Single(results, r => !r.Conflict); Assert.Single(results, r => r.Conflict); Assert.Equal(results[0].Current, results[1].Current);
        Assert.Equal(results[0], await Applied(a)); Assert.Equal(results[1], await Applied(b));
    }
    [PostgresFact]
    public async Task Planning_LegacyDependencyReceiptReplayDoesNotOverwriteNewerClassification()
    {
        As(await Register("planning-receipt@example.invalid"));
        var legacy = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false,
            new("Legacy", Tags: TagSet.Parse("work"), Parent: new(null), Prerequisites: Prerequisites()));
        Assert.DoesNotContain("Quadrant", JsonSerializer.Serialize(legacy)); var original = await Applied(legacy);
        var oldResult = original with { Current = original.Current with { Task = original.Current.Task! with { Quadrant = null } } };
        await using (var db = Context()) { var receipt = await db.SyncReceipts.SingleAsync(); receipt.Response = JsonSerializer.Serialize(oldResult); await db.SaveChangesAsync(); }
        using var changed = await Plan(legacy.TaskId, EisenhowerQuadrant.Neither); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(oldResult, await Applied(legacy)); Assert.Equal(EisenhowerQuadrant.Neither, (await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{legacy.TaskId}", ClientHarness.JsonOptions))!.Quadrant);
    }
    [PostgresFact]
    public async Task Planning_TwoReplicasConvergeThroughConflictRepairAndExplicitUnclassified()
    {
        var account = await Register("planning-replicas@example.invalid"); var settings = new MemoryClientSettings { ApiBaseUrl = "https://localhost/" };
        var session = new ClientSession(settings, new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken, account.TokenType, account.ExpiresAt, new(account.User.Id, account.User.Email)), new Uri(settings.ApiBaseUrl), session.Version);
        var api = new TodoApiClient(client, session); var directory = Path.Combine(Path.GetTempPath(), "todoapp_planning_replicas_" + Guid.NewGuid().ToString("N"));
        var a = new SqliteSyncStore(Path.Combine(directory, "a.db3")); var b = new SqliteSyncStore(Path.Combine(directory, "b.db3"));
        var scope = SqliteSyncStore.Partition(settings.ApiBaseUrl, account.User.Id); var id = Guid.NewGuid(); var engineA = new SyncEngine(session, api, a); var engineB = new SyncEngine(session, api, b);
        try
        {
            await a.QueueAsync(scope, id, new("Task", Quadrant: EisenhowerQuadrant.UrgentImportant)); await engineA.RunAsync(); await engineB.RunAsync();
            Assert.Equal(EisenhowerQuadrant.UrgentImportant, (await b.ReadAsync(scope)).Rows[id].Local!.Quadrant);
            await a.QueueAsync(scope, id, new("Remote fields", Quadrant: EisenhowerQuadrant.UrgentNotImportant));
            await b.QueueAsync(scope, id, new("Local fields", Quadrant: EisenhowerQuadrant.ImportantNotUrgent)); await engineA.RunAsync(); await engineB.RunAsync();
            var row = (await b.ReadAsync(scope)).Rows[id]; Assert.True(row.Conflict); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, row.Local!.Quadrant); Assert.Equal(EisenhowerQuadrant.UrgentNotImportant, row.Remote!.Task!.Quadrant);
            await b.SetQuadrantAsync(scope, id, EisenhowerQuadrant.ImportantNotUrgent, row.Remote.Revision, row.Local, true); await engineB.RunAsync(); await engineA.RunAsync();
            row = (await a.ReadAsync(scope)).Rows[id]; Assert.Equal("Local fields", row.Local!.Title); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, row.Local.Quadrant); Assert.Null(row.Pending);
            await b.QueueAsync(scope, id, new("Local fields", Quadrant: EisenhowerQuadrant.Unclassified)); await engineB.RunAsync(); await engineA.RunAsync();
            row = (await a.ReadAsync(scope)).Rows[id]; Assert.Equal(EisenhowerQuadrant.Unclassified, row.Local!.Quadrant); Assert.False(row.Conflict); Assert.Null(row.Pending);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
