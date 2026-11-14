using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class DependencyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_dependencies_" + Guid.NewGuid().ToString("N"));
    private string FileName(string name) => Path.Combine(directory, name);
    private static TaskPrerequisites Set(params Guid[] ids) => TaskPrerequisites.FromIds(ids);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void Prerequisites_AreImmutableCanonicalValueEqualAndBounded()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var source = new[] { b, a, b }; var set = TaskPrerequisites.FromIds(source); source[0] = Guid.NewGuid();
        Assert.Equal(Set(a, b), set); Assert.Equal(Set(a, b).GetHashCode(), set.GetHashCode()); Assert.Equal(2, set.Count);
        Assert.Equal(set, JsonSerializer.Deserialize<TaskPrerequisites>(JsonSerializer.Serialize(set)));
        Assert.Throws<ArgumentException>(() => Set(Guid.Empty));
        Assert.Throws<ArgumentException>(() => TaskPrerequisites.FromIds(Enumerable.Range(0, 33).Select(_ => Guid.NewGuid())));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[\"bad-guid\"]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    public void MalformedPrerequisites_AreRejected(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TaskPrerequisites>(json));

    [Fact]
    public void Graph_RejectsCyclesMissingAndSelfAcceptsDiamondAndLongIndependentChains()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToArray();
        var graph = ids.Select((id, index) => new DependencyNode(id, index == 0 ? Set() : Set(ids[index - 1]), false)).ToArray();
        TaskDependencies.ValidateChange(graph, ids[^1], Set(ids[^2])); // Dependency depth is independent of hierarchy's 16 levels.
        Assert.Throws<TaskDependencyException>(() => TaskDependencies.ValidateChange(graph, ids[0], Set(ids[^1])));
        Assert.Throws<TaskDependencyException>(() => TaskDependencies.ValidateChange(graph, ids[0], Set(ids[0])));
        Assert.Throws<TaskDependencyException>(() => TaskDependencies.ValidateChange(graph, ids[0], Set(Guid.NewGuid())));
        var diamond = new[] { new DependencyNode(ids[0], Set(), true), new(ids[1], Set(ids[0]), true), new(ids[2], Set(ids[0]), true), new(ids[3], Set(), false) };
        TaskDependencies.ValidateChange(diamond, ids[3], Set(ids[1], ids[2]), true);
        Assert.Throws<TaskDependencyException>(() => TaskDependencies.ValidateDelete(diamond, ids[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Services_EnforceCompletionAndDeletionPreserveFieldsAndAllowExplicitClear(bool sqlite)
    {
        ILocalTaskService service = sqlite ? new SqliteLocalTaskService(FileName("tasks.db3")) : new InMemoryLocalTaskService();
        var prerequisite = await service.CreateAsync(new("First")); var root = await service.CreateAsync(new("Project"));
        var consumer = await service.CreateAsync(new("Second", "Keep", Tags: TagSet.Parse("work"), Parent: new(root.Id), Prerequisites: Set(prerequisite.Id)));
        await Assert.ThrowsAsync<TaskDependencyException>(() => service.SetCompletedAsync(consumer.Id, true));
        await Assert.ThrowsAsync<TaskDependencyException>(() => service.DeleteAsync(prerequisite.Id));
        await Assert.ThrowsAsync<TaskDependencyException>(() => service.SetPrerequisitesAsync(prerequisite.Id, Set(consumer.Id)));
        await Assert.ThrowsAsync<TaskDependencyException>(() => service.UpdateAsync(consumer.Id, new("Wrong", Prerequisites: Set(Guid.NewGuid()))));
        Assert.Equal("Second", (await service.GetByIdAsync(consumer.Id))!.Title);
        await service.UpdateAsync(consumer.Id, new("Edited", "Keep", Tags: TagSet.Parse("work")));
        Assert.Equal(Set(prerequisite.Id), (await service.GetByIdAsync(consumer.Id))!.Prerequisites);
        await service.SetCompletedAsync(prerequisite.Id, true); await service.SetCompletedAsync(consumer.Id, true);
        await service.SetCompletedAsync(prerequisite.Id, false); Assert.True((await service.GetByIdAsync(consumer.Id))!.IsCompleted);
        var other = await service.CreateAsync(new("Unfinished"));
        await Assert.ThrowsAsync<TaskDependencyException>(() => service.SetPrerequisitesAsync(consumer.Id, Set(other.Id)));
        await service.SetPrerequisitesAsync(consumer.Id, Set()); var cleared = (await service.GetByIdAsync(consumer.Id))!;
        Assert.Empty(cleared.Prerequisites); Assert.Equal(root.Id, cleared.ParentTaskId); Assert.Equal(consumer.Tags, cleared.Tags); Assert.Equal(consumer.CreatedAt, cleared.CreatedAt);
        Assert.True(await service.DeleteAsync(prerequisite.Id));
        if (sqlite) Assert.Empty((await new SqliteLocalTaskService(FileName("tasks.db3")).GetByIdAsync(consumer.Id))!.Prerequisites);
    }

    [Fact]
    public async Task SQLiteVersionThreeUpgrade_PreservesHierarchyTagsAndTimestamps()
    {
        Directory.CreateDirectory(directory); var path = FileName("old.db3"); var parent = Guid.NewGuid(); var child = Guid.NewGuid(); var time = DateTimeOffset.UtcNow;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
                CREATE TABLE TaskItems (Id TEXT PRIMARY KEY, Title TEXT, Description TEXT, IsCompleted INTEGER, DueDate TEXT,
                    Priority INTEGER, CategoryId TEXT, CreatedAt TEXT, UpdatedAt TEXT, Tags TEXT, ParentTaskId TEXT);
                INSERT INTO TaskItems VALUES ($parent,'Parent',NULL,0,NULL,1,NULL,$time,$time,'[]',NULL);
                INSERT INTO TaskItems VALUES ($child,'Child','Keep',1,NULL,2,NULL,$time,$time,'["work"]',$parent);
                PRAGMA user_version=3;
                """;
            command.Parameters.AddWithValue("$parent", parent.ToString()); command.Parameters.AddWithValue("$child", child.ToString()); command.Parameters.AddWithValue("$time", time.ToString("O")); command.ExecuteNonQuery();
        }
        var restored = (await new SqliteLocalTaskService(path).GetByIdAsync(child))!;
        Assert.Empty(restored.Prerequisites); Assert.Equal(parent, restored.ParentTaskId); Assert.Equal(TagSet.Parse("work"), restored.Tags);
        Assert.Equal(time, restored.UpdatedAt); Assert.Equal(time, restored.CreatedAt); Assert.True(restored.IsCompleted);
    }

    [Fact]
    public async Task ConcurrentSQLiteOppositeEdges_HaveOnlyOneWinner()
    {
        var path = FileName("tasks.db3"); var a = new SqliteLocalTaskService(path); var b = new SqliteLocalTaskService(path);
        var first = await a.CreateAsync(new("A")); var second = await b.CreateAsync(new("B"));
        async Task<bool> Try(ILocalTaskService service, Guid id, Guid dependency)
        { try { await service.SetPrerequisitesAsync(id, Set(dependency)); return true; } catch (TaskDependencyException) { return false; } }
        var results = await Task.WhenAll(Try(a, first.Id, second.Id), Try(b, second.Id, first.Id)); Assert.Single(results, value => value);
    }

    [Fact]
    public async Task CachedEdits_FreezeOperationsPreserveOmittedLinksAndRejectBlockedCompletion()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var prerequisite = Guid.NewGuid(); var id = Guid.NewGuid();
        await store.QueueAsync("a", prerequisite, new("First", Prerequisites: Set()));
        await store.QueueAsync("a", id, new("Second", Prerequisites: Set(prerequisite))); var frozen = (await store.ReadAsync("a")).Rows[id].Pending!;
        await store.QueueAsync("a", id, new("Later")); Assert.Equal(frozen, (await store.ReadAsync("a")).Rows[id].Pending);
        await Assert.ThrowsAsync<TaskDependencyException>(() => store.QueueAsync("a", id, new("Later", IsCompleted: true)));
        await Assert.ThrowsAsync<TaskDependencyException>(() => store.QueueAsync("a", prerequisite, null));
        await store.AcknowledgeAsync("a", frozen, new(false, new(id, 1, false, frozen.Task)));
        var next = (await store.ReadAsync("a")).Rows[id].Pending!; Assert.Equal(Set(prerequisite), next.Task!.Prerequisites); Assert.Equal("Later", next.Task.Title);
        await store.QueueAsync("a", id, new("Cleared", Prerequisites: Set())); Assert.Equal(next, (await store.ReadAsync("a")).Rows[id].Pending);
        await store.AcknowledgeAsync("a", next, new(false, new(id, 2, false, next.Task)));
        Assert.Empty((await store.ReadAsync("a")).Rows[id].Pending!.Task!.Prerequisites!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolThreeApi_BlocksAndRetainsDependencyOperationOrDeletion(bool deleting)
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, deleting ? null : new("Root", Prerequisites: Set())); var pending = (await store.ReadAsync(scope)).Rows[id].Pending;
        client.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([], 0, false, 3)));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(client.Session, client.Api, store, client.Clock).RunAsync());
        Assert.Equal(pending, (await store.ReadAsync(scope)).Rows[id].Pending); Assert.Equal(DateTimeOffset.MaxValue, (await store.ReadAsync(scope)).Rows[id].RetryAt);
        Assert.DoesNotContain(client.Handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/api/sync");
    }

    [Fact]
    public async Task LegacyVersionThreeCacheUpgrade_DoesNotRewritePendingReceiptBytes()
    {
        Directory.CreateDirectory(directory); var path = FileName("sync.db3"); var id = Guid.NewGuid();
        var operation = new SyncMutation(Guid.NewGuid(), id, 0, false, new("Legacy", Tags: TagSet.Parse("work"), Parent: new(null)));
        var bytes = JsonSerializer.Serialize(operation); Assert.DoesNotContain("Prerequisites", bytes);
        var state = new ReplicaState(); state.Rows[id] = new() { Id = id, Local = operation.Task, Pending = operation }; var json = JsonSerializer.Serialize(state);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE ReplicaPartitions (Partition TEXT PRIMARY KEY, State TEXT NOT NULL); INSERT INTO ReplicaPartitions VALUES ('a',$json); PRAGMA user_version=3;";
            command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
        }
        Assert.Equal(bytes, JsonSerializer.Serialize((await new SqliteSyncStore(path).ReadAsync("a")).Rows[id].Pending));
        using var check = new SqliteConnection($"Data Source={path};Pooling=False"); check.Open(); using var query = check.CreateCommand();
        query.CommandText = "SELECT State FROM ReplicaPartitions"; Assert.Equal(json, query.ExecuteScalar());
        query.CommandText = "PRAGMA user_version"; Assert.Equal(7L, query.ExecuteScalar());
    }

    [Fact]
    public async Task DependencyScreen_SavesLinksShowsBlockingAndExcludesSelfWithoutChangingParentOrTags()
    {
        using var client = new ClientHarness(); var local = new InMemoryLocalTaskService(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var first = await local.CreateAsync(new("First")); var consumer = await local.CreateAsync(new("Second", Tags: TagSet.Parse("work"), Parent: new(first.Id)));
        using var vm = new DependenciesViewModel(local, store, client.Session, new ClientNavigation()); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = vm.Tasks.Single(t => t.Id == consumer.Id); Assert.DoesNotContain(vm.Choices, c => c.Id == consumer.Id);
        vm.Candidate = vm.Choices.Single(c => c.Id == first.Id); await vm.AddCommand.ExecuteAsync(); Assert.Empty((await local.GetByIdAsync(consumer.Id))!.Prerequisites);
        await vm.SaveCommand.ExecuteAsync(); Assert.Contains("Completion blocked", vm.Tasks.Single(t => t.Id == consumer.Id).Status);
        var saved = (await local.GetByIdAsync(consumer.Id))!; Assert.Equal(Set(first.Id), saved.Prerequisites); Assert.Equal(first.Id, saved.ParentTaskId); Assert.Equal(consumer.Tags, saved.Tags);
        vm.SelectedTask = vm.Tasks.Single(t => t.Id == consumer.Id); await vm.ClearCommand.ExecuteAsync(); await vm.SaveCommand.ExecuteAsync();
        Assert.Empty((await local.GetByIdAsync(consumer.Id))!.Prerequisites);
    }

    [Fact]
    public async Task ConflictRepair_RequiresConfirmationRejectsStaleLocalAndClearsAccountDuringConfirmation()
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Local", Prerequisites: Set())); var pending = (await store.ReadAsync(scope)).Rows[id].Pending!;
        await store.AcknowledgeAsync(scope, pending, new(true, new(id, 1, false, new("Remote", Prerequisites: Set())), "Review dependencies"));
        var row = (await store.ReadAsync(scope)).Rows[id];
        await Assert.ThrowsAsync<TaskDependencyException>(() => store.SetPrerequisitesAsync(scope, id, Set(), 0, row.Local!, true));
        await Assert.ThrowsAsync<TaskDependencyException>(() => store.SetPrerequisitesAsync(scope, id, Set(), 1, new("Other"), true));
        var navigation = new ClientNavigation(); using var vm = new DependenciesViewModel(new InMemoryLocalTaskService(), store, client.Session, navigation) { Workspace = HierarchyWorkspace.SyncedAccount };
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks);
        await vm.KeepLocalCommand.ExecuteAsync(); Assert.True((await store.ReadAsync(scope)).Rows[id].Conflict);
        navigation.Confirm = true; await vm.KeepLocalCommand.ExecuteAsync(); var repaired = (await store.ReadAsync(scope)).Rows[id];
        Assert.False(repaired.Conflict); Assert.Equal("Local", repaired.Local!.Title); Assert.Equal(1, repaired.Pending!.BaseRevision);
        await store.AcknowledgeAsync(scope, repaired.Pending, new(true, new(id, 2, false, new("New remote", Prerequisites: Set()))));
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks);
        navigation.DependencyConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.KeepLocalCommand.ExecuteAsync(); Assert.Empty(vm.Tasks); Assert.Empty(vm.Draft); Assert.True((await store.ReadAsync(scope)).Rows[id].Conflict);
    }
}
