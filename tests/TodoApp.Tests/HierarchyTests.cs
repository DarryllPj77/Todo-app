using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class HierarchyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_hierarchy_" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(directory, name);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void Rules_RejectSelfMissingCycleAndSubtreeMovesBeyondSixteenLevels()
    {
        var ids = Enumerable.Range(0, 17).Select(_ => Guid.NewGuid()).ToArray();
        var tree = ids.Take(16).Select((id, i) => new TaskLink(id, i == 0 ? null : ids[i - 1])).Append(new(ids[16], null)).ToArray();
        TaskHierarchy.ValidateMove(tree, ids[15], ids[14]);
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateMove(tree, ids[0], ids[0]));
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateMove(tree, ids[0], ids[15]));
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateMove(tree, ids[16], ids[15]));
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateMove(tree, ids[0], ids[16])); // All descendants count.
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateMove(tree, ids[0], Guid.NewGuid()));
        Assert.Throws<TaskHierarchyException>(() => TaskHierarchy.ValidateDelete(tree, ids[0]));
        TaskHierarchy.ValidateDelete(tree, ids[15]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Services_PreserveFieldsRejectCyclesRestrictDeletionAndDetachExplicitly(bool sqlite)
    {
        ILocalTaskService service = sqlite ? new SqliteLocalTaskService(PathFor("tasks.db3")) : new InMemoryLocalTaskService();
        var parent = await service.CreateAsync(new("Parent"));
        var child = await service.CreateAsync(new("Child", "Keep", new(2026, 11, 20), TaskPriority.High,
            new Category("Work"), TagSet.Parse("work"), new(parent.Id)));
        await service.SetCompletedAsync(parent.Id, true);
        Assert.False((await service.GetByIdAsync(child.Id))!.IsCompleted);
        await Assert.ThrowsAsync<TaskHierarchyException>(() => service.SetParentAsync(parent.Id, child.Id));
        await Assert.ThrowsAsync<TaskHierarchyException>(() => service.DeleteAsync(parent.Id));
        await Assert.ThrowsAsync<TaskHierarchyException>(() => service.UpdateAsync(child.Id, new("Wrong", Parent: new(Guid.NewGuid()))));
        Assert.Equal("Child", (await service.GetByIdAsync(child.Id))!.Title);
        await service.UpdateAsync(child.Id, new("Edited", child.Description, child.DueDate, child.Priority, child.Category));
        var edited = (await service.GetByIdAsync(child.Id))!;
        Assert.Equal(parent.Id, edited.ParentTaskId); Assert.Equal(child.Tags, edited.Tags);
        await service.SetParentAsync(child.Id, null);
        var detached = (await service.GetByIdAsync(child.Id))!;
        Assert.Null(detached.ParentTaskId); Assert.Equal(edited.Title, detached.Title); Assert.Equal(edited.CategoryId, detached.CategoryId);
        Assert.Equal(edited.DueDate, detached.DueDate); Assert.Equal(edited.Tags, detached.Tags); Assert.Equal(edited.CreatedAt, detached.CreatedAt);
        Assert.True(await service.DeleteAsync(parent.Id)); Assert.NotNull(await service.GetByIdAsync(child.Id));
        if (sqlite) Assert.Null((await new SqliteLocalTaskService(PathFor("tasks.db3")).GetByIdAsync(child.Id))!.ParentTaskId);
    }

    [Fact]
    public async Task SQLiteVersionTwoUpgrade_PreservesTagsAndTimesAndEnforcesForeignKey()
    {
        Directory.CreateDirectory(directory); var path = PathFor("old.db3"); var id = Guid.NewGuid(); var time = DateTimeOffset.UtcNow;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
                CREATE TABLE TaskItems (Id TEXT PRIMARY KEY, Title TEXT, Description TEXT, IsCompleted INTEGER, DueDate TEXT,
                    Priority INTEGER, CategoryId TEXT, CreatedAt TEXT, UpdatedAt TEXT, Tags TEXT NOT NULL DEFAULT '[]');
                INSERT INTO TaskItems VALUES ($id,'Legacy','Keep',0,NULL,1,NULL,$time,$time,'["work"]');
                PRAGMA user_version=2;
                """;
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$time", time.ToString("O")); command.ExecuteNonQuery();
        }
        var service = new SqliteLocalTaskService(path); var restored = Assert.Single(await service.GetTasksAsync());
        Assert.Null(restored.ParentTaskId); Assert.Equal(time, restored.UpdatedAt); Assert.Equal(TagSet.Parse("work"), restored.Tags);
        var child = await service.CreateAsync(new("Child", Parent: new(id)));
        using var check = new SqliteConnection($"Data Source={path};Pooling=False;Foreign Keys=True"); check.Open(); using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;"; Assert.Equal(4L, query.ExecuteScalar());
        query.CommandText = "DELETE FROM TaskItems WHERE Id=$id"; query.Parameters.AddWithValue("$id", id.ToString());
        Assert.Throws<SqliteException>(() => query.ExecuteNonQuery()); Assert.NotNull(await service.GetByIdAsync(child.Id));
    }

    [Fact]
    public async Task CachedMoves_FreezePayloadPreserveLaterEditsAndGuardStaleRepair()
    {
        var store = new SqliteSyncStore(PathFor("sync.db3")); const string scope = "account";
        var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        await store.QueueAsync(scope, parent, new("Parent", Parent: new(null)));
        await store.QueueAsync(scope, child, new("Child", Tags: TagSet.Parse("work"), Parent: new(parent)));
        var frozen = (await store.ReadAsync(scope)).Rows[child].Pending!;
        await store.QueueAsync(scope, child, new("Later", Tags: TagSet.Parse("urgent")));
        Assert.Equal(frozen, (await store.ReadAsync(scope)).Rows[child].Pending);
        await store.AcknowledgeAsync(scope, frozen, new(false, new(child, 2, false, frozen.Task)));
        var next = (await store.ReadAsync(scope)).Rows[child].Pending!;
        Assert.NotEqual(frozen.OperationId, next.OperationId); Assert.Equal(parent, next.Task!.Parent!.Id); Assert.Equal("Later", next.Task.Title);
        await store.AcknowledgeAsync(scope, next, new(true, new(child, 2, false, frozen.Task), "Choose another parent"));
        var conflict = (await store.ReadAsync(scope)).Rows[child]; Assert.True(conflict.Conflict); Assert.Equal("Choose another parent", conflict.Summary);
        await Assert.ThrowsAsync<TaskHierarchyException>(() => store.ReparentAsync(scope, child, null, 2, conflict.Local!));
        await Assert.ThrowsAsync<TaskHierarchyException>(() => store.ReparentAsync(scope, child, null, 1, conflict.Local!, true));
        await store.ReparentAsync(scope, child, null, 2, conflict.Local!, true);
        var repaired = (await store.ReadAsync(scope)).Rows[child]; Assert.False(repaired.Conflict); Assert.Null(repaired.ConflictReason);
        Assert.Null(repaired.Local!.Parent!.Id); Assert.Equal("Later", repaired.Local.Title); Assert.Equal(TagSet.Parse("urgent"), repaired.Local.Tags);
        Assert.Equal(2, repaired.Pending!.BaseRevision);
    }

    [Fact]
    public async Task CachedDelete_RequiresLiveChildrenToBeMovedOrDeleted()
    {
        var store = new SqliteSyncStore(PathFor("sync.db3")); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        await store.QueueAsync("a", parent, new("Parent")); await store.QueueAsync("a", child, new("Child", Parent: new(parent)));
        await Assert.ThrowsAsync<TaskHierarchyException>(() => store.QueueAsync("a", parent, null));
        await store.QueueAsync("a", child, null); await store.QueueAsync("a", parent, null);
        Assert.All((await store.ReadAsync("a")).Rows.Values, row => Assert.Null(row.Local));
    }

    [Fact]
    public async Task ProtocolTwoApi_RetainsHierarchyOperationWithoutUploading()
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(PathFor("sync.db3"));
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Root", Parent: new(null))); var pending = (await store.ReadAsync(scope)).Rows[id].Pending;
        client.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([], 0, false, 2)));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(client.Session, client.Api, store, client.Clock).RunAsync());
        Assert.Equal(pending, (await store.ReadAsync(scope)).Rows[id].Pending);
        Assert.Equal(DateTimeOffset.MaxValue, (await store.ReadAsync(scope)).Rows[id].RetryAt);
        Assert.DoesNotContain(client.Handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/api/sync");
    }

    [Fact]
    public void LegacyTaggedMutationSerialization_OmitsParentAndKeepsReceiptBytes()
    {
        var old = new SyncMutation(Guid.NewGuid(), Guid.NewGuid(), 0, false, new("Legacy", Tags: TagSet.Parse("work")));
        var bytes = JsonSerializer.Serialize(old); Assert.DoesNotContain("Parent", bytes);
        Assert.Equal(bytes, JsonSerializer.Serialize(JsonSerializer.Deserialize<SyncMutation>(bytes)));
        Assert.Contains("\"Parent\":{\"Id\":null}", JsonSerializer.Serialize(old with { Task = old.Task! with { Parent = new(null) } }));
    }

    [Fact]
    public async Task HierarchyScreen_ExcludesDescendantsMovesOnlyParentAndClearsAccountOnLogout()
    {
        using var client = new ClientHarness(); var service = new InMemoryLocalTaskService(); var store = new SqliteSyncStore(PathFor("sync.db3"));
        var root = await service.CreateAsync(new("Root")); var child = await service.CreateAsync(new("Child", Tags: TagSet.Parse("work"), Parent: new(root.Id)));
        using var vm = new HierarchyViewModel(service, store, client.Session, new ClientNavigation());
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(new[] { root.Id, child.Id }, vm.Rows.Select(r => r.Id)); Assert.Equal(2, vm.Rows[1].Level);
        vm.SelectedTask = vm.Rows[0]; Assert.Single(vm.Parents); // Root cannot choose its descendant.
        vm.SelectedTask = vm.Rows[1]; vm.SelectedParent = vm.Parents.Single(p => p.Id is null); await vm.MoveCommand.ExecuteAsync();
        var saved = (await service.GetByIdAsync(child.Id))!; Assert.Null(saved.ParentTaskId); Assert.Equal(child.Tags, saved.Tags);
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        await store.QueueAsync(scope, Guid.NewGuid(), new("Private")); vm.Workspace = HierarchyWorkspace.SyncedAccount;
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal("Private", Assert.Single(vm.Rows).Title);
        await client.Session.SignOutAsync(); Assert.Empty(vm.Rows); Assert.Null(vm.SelectedTask);
        Assert.Single((await store.ReadAsync(scope)).Rows);
    }
}
