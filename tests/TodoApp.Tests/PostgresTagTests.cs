using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests
{
    [PostgresFact]
    public async Task Tags_RestCrudNormalizesPreservesClearsAndEnforcesAccountOwnership()
    {
        var alice = await Register("tags-alice@example.invalid"); var bob = await Register("tags-bob@example.invalid"); As(alice);
        using var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Tagged", tags = new[] { " Work ", "URGENT", "work" }, categoryName = "Work" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        Assert.Equal(TagSet.Parse("urgent,work"), task.Tags); var id = task.Id;
        using var legacyUpdate = await client.PutAsJsonAsync($"/api/tasks/{id}", new { title = "Legacy edit" });
        Assert.Equal(HttpStatusCode.OK, legacyUpdate.StatusCode);
        Assert.Equal(task.Tags, (await legacyUpdate.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Tags);
        using var nullUpdate = await client.PutAsJsonAsync($"/api/tasks/{id}", new { title = "Null preserves", tags = (string[]?)null });
        Assert.Equal(task.Tags, (await nullUpdate.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Tags);
        As(bob); using var hidden = await client.GetAsync($"/api/tasks/{id}"); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var own = await client.PostAsJsonAsync("/api/tasks", new { title = "Bob", tags = new[] { "work" } }); Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        As(alice); using var cleared = await client.PutAsJsonAsync($"/api/tasks/{id}", new { title = "Cleared", tags = Array.Empty<string>() });
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Tags!);
        var page = await Download(); Assert.Equal(4, page.ProtocolVersion);
        Assert.Empty(Assert.Single(page.Changes).Task!.Tags!);
    }
    [PostgresFact]
    public async Task Tags_InvalidArraysAndPrivilegedFields_DoNotPartiallyUpdateTasks()
    {
        As(await Register("tags-invalid@example.invalid"));
        using var created = await client.PostAsJsonAsync("/api/tasks", new { title = "Keep", tags = new[] { "work" } });
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        var invalidInputs = new object[] { new { title = "Changed", tags = Enumerable.Range(0,11).Select(i => $"tag{i}").ToArray() },
            new { title = "Changed", tags = new[] { "" } }, new { title = "Changed", tags = new[] { new string('x',33) } },
            new { title = "Changed", tags = "not an array" }, new { title = "Changed", tags = new[] { "new" }, ownerId = Guid.NewGuid() } };
        foreach (var input in invalidInputs)
        {
            using var rejected = await client.PutAsJsonAsync($"/api/tasks/{task.Id}", input);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(task, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}", ClientHarness.JsonOptions));
        await using var database = Context(); Assert.Single(await database.Tasks.ToListAsync());
    }
    [PostgresFact]
    public async Task Tags_MigrationPreservesExistingOwnedTasksAndInitializesEmptyValues()
    {
        var account = await Register("tags-migration@example.invalid"); var id = Guid.NewGuid();
        await using (var database = Context())
        {
            await database.GetService<IMigrator>().MigrateAsync("AddTaskSynchronization");
            var raw = DateTimeOffset.UtcNow; var now = new DateTimeOffset(raw.UtcTicks - raw.UtcTicks % 10, TimeSpan.Zero);
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Tasks" ("Id","Title","Description","IsCompleted","DueDate","Priority","CreatedAt","UpdatedAt","OwnerId")
                VALUES ({id},'Legacy','Keep',true,'2026-11-20',2,{now},{now},{account.User.Id});
                """);
            await database.Database.MigrateAsync();
            var task = await database.Tasks.AsNoTracking().SingleAsync();
            Assert.Equal(id, task.Id); Assert.Equal("Keep", task.Description); Assert.True(task.IsCompleted); Assert.Empty(task.Tags);
            Assert.Equal(now, task.CreatedAt); Assert.Equal(now, task.UpdatedAt); Assert.Equal(new DateOnly(2026,11,20), task.DueDate);
            Assert.False(database.Database.HasPendingModelChanges()); Assert.Equal(6, (await database.Database.GetAppliedMigrationsAsync()).Count());
        }
        As(account); Assert.Empty(Assert.Single((await Download()).Changes).Task!.Tags!);
    }
    [PostgresFact]
    public async Task Tags_LegacyReceiptReplaySurvivesTheNewContractWithoutClobberingNewerTags()
    {
        As(await Register("tags-receipt@example.invalid"));
        var input = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Legacy operation"));
        var original = await Applied(input);
        // Model a receipt stored before Tags existed, including the old response shape.
        var oldResult = original with { Current = original.Current with { Task = original.Current.Task! with { Tags = null } } };
        await using (var database = Context())
        {
            var receipt = await database.SyncReceipts.SingleAsync(); receipt.Request = JsonSerializer.Serialize(input);
            receipt.Response = JsonSerializer.Serialize(oldResult); await database.SaveChangesAsync();
        }
        using var newer = await client.PutAsJsonAsync($"/api/tasks/{input.TaskId}", new { title = "Newer", tags = new[] { "important" } });
        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);
        Assert.Equal(oldResult, await Applied(input));
        var live = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{input.TaskId}", ClientHarness.JsonOptions);
        Assert.Equal(TagSet.Parse("important"), live!.Tags);
    }
    [PostgresFact]
    public async Task Tags_TwoReplicasConvergePreserveConflictSetsAndClearWithoutEndlessUploads()
    {
        var account = await Register("tags-replicas@example.invalid");
        var settings = new MemoryClientSettings { ApiBaseUrl = "https://localhost/" };
        var session = new ClientSession(settings, new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken, account.TokenType, account.ExpiresAt,
            new ApiUser(account.User.Id, account.User.Email)), new Uri(settings.ApiBaseUrl), session.Version);
        var api = new TodoApiClient(client, session);
        var directory = Path.Combine(Path.GetTempPath(), "todoapp_tag_replicas_" + Guid.NewGuid().ToString("N"));
        var a = new SqliteSyncStore(Path.Combine(directory,"a.db3")); var b = new SqliteSyncStore(Path.Combine(directory,"b.db3"));
        var scope = SqliteSyncStore.Partition(settings.ApiBaseUrl, account.User.Id); var id = Guid.NewGuid();
        var engineA = new SyncEngine(session, api, a); var engineB = new SyncEngine(session, api, b);
        try
        {
            await a.QueueAsync(scope, id, new("Tagged", Tags: TagSet.Parse("work,urgent")));
            await engineA.RunAsync(); Assert.Null((await a.ReadAsync(scope)).Rows[id].Pending); await engineB.RunAsync();
            Assert.Equal(TagSet.Parse("work,urgent"), (await b.ReadAsync(scope)).Rows[id].Local!.Tags);
            await a.QueueAsync(scope, id, new("Tagged", Tags: TagSet.Parse("remote")));
            await b.QueueAsync(scope, id, new("Tagged", Tags: TagSet.Parse("local")));
            await engineA.RunAsync(); await engineB.RunAsync();
            var conflict = (await b.ReadAsync(scope)).Rows[id]; Assert.True(conflict.Conflict);
            Assert.Equal(TagSet.Parse("local"), conflict.Local!.Tags); Assert.Equal(TagSet.Parse("remote"), conflict.Remote!.Task!.Tags);
            await b.ResolveAsync(scope, id, false); await engineB.RunAsync();
            Assert.Equal(TagSet.Parse("remote"), (await b.ReadAsync(scope)).Rows[id].Local!.Tags);
            await b.QueueAsync(scope, id, new("Tagged", Tags: TagSet.Empty)); await engineB.RunAsync(); await engineA.RunAsync();
            Assert.Empty((await a.ReadAsync(scope)).Rows[id].Local!.Tags!); Assert.Null((await b.ReadAsync(scope)).Rows[id].Pending);
            await using var database = Context(); Assert.Equal(4, await database.SyncReceipts.CountAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
}
