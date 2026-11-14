using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Domain.Views;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class TagTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_tags_" + Guid.NewGuid().ToString("N"));
    private string FileName(string name) => Path.Combine(directory, name);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static TagSet Tags(string input) => TagSet.Parse(input);
    [Fact]
    public void TagSet_IsImmutableCanonicalAndValueEqualAcrossJsonRoundTrips()
    {
        var input = new[] { " Work ", "URGENT", "work" }; var tags = TagSet.FromNames(input); input[0] = "changed";
        Assert.Equal(new[] { "urgent", "work" }, tags);
        var reopened = JsonSerializer.Deserialize<TagSet>(JsonSerializer.Serialize(tags));
        Assert.Equal(tags, reopened); Assert.Equal(tags.GetHashCode(), reopened!.GetHashCode());
        Assert.Equal("urgent, work", tags.ToString());
        Assert.Equal(Tags("caf\u00e9"), Tags("cafe\u0301"));
    }
    [Theory]
    [InlineData("""[null]""")]
    [InlineData("""[1]""")]
    [InlineData("""[""]""")]
    [InlineData("""["a,b"]""")]
    [InlineData("""{"work":true}""")]
    [InlineData("""["a\nb"]""")]
    public void InvalidJsonTagValues_AreRejected(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TagSet>(json));
    [Fact]
    public void Tags_EnforceBoundedCountAndNameLength()
    {
        Assert.Throws<ArgumentException>(() => Tags(new string('x', 33)));
        Assert.Throws<ArgumentException>(() => TagSet.FromNames(Enumerable.Range(0, 11).Select(i => $"tag{i}")));
        Assert.Equal(10, TagSet.FromNames(Enumerable.Range(0, 10).Select(i => $"tag{i}")).Count);
        Assert.Equal(TagSet.Empty, Tags(" , , "));
    }
    [Fact]
    public void DomainChanges_PreserveOmittedTagsClearExplicitlyAndKeepNoOpsStable()
    {
        var task = new TaskItem("Keep", tags: Tags("work")); var timestamp = task.UpdatedAt;
        task.SetTags(Tags("WORK")); Assert.Equal(timestamp, task.UpdatedAt);
        task.UpdateDetails("Keep", null, null, TaskPriority.Normal); Assert.Equal(Tags("work"), task.Tags);
        Assert.Throws<ArgumentException>(() => task.UpdateDetails(" ", null, null, TaskPriority.Normal, TagSet.Empty));
        Assert.Equal(Tags("work"), task.Tags); Assert.Equal("Keep", task.Title);
        task.UpdateDetails("Keep", null, null, TaskPriority.Normal, TagSet.Empty); Assert.Empty(task.Tags);
    }
    [Fact]
    public async Task SQLiteTags_PersistClearAndSurviveCompletionWithoutChangingCategory()
    {
        var path = FileName("tasks.db3"); var service = new SqliteLocalTaskService(path); var category = new Category("Work");
        var task = await service.CreateAsync(new TaskDetails("Keep", Category: category, Tags: Tags("work, important")));
        var reopened = await new SqliteLocalTaskService(path).GetByIdAsync(task.Id);
        Assert.Equal(task.Tags, reopened!.Tags); Assert.Equal(category.Id, reopened.CategoryId);
        await service.UpdateAsync(task.Id, new TaskDetails("Edited", Category: category)); await service.SetCompletedAsync(task.Id, true);
        Assert.Equal(task.Tags, (await service.GetByIdAsync(task.Id))!.Tags);
        await service.UpdateAsync(task.Id, new TaskDetails("Edited", Category: category, Tags: TagSet.Empty));
        Assert.Empty((await service.GetByIdAsync(task.Id))!.Tags);
    }
    [Fact]
    public async Task VersionOneSQLiteUpgrade_PreservesExistingTaskAndAddsEmptyTags()
    {
        Directory.CreateDirectory(directory); var path = FileName("old.db3"); var id = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
                CREATE TABLE TaskItems (Id TEXT PRIMARY KEY, Title TEXT, Description TEXT, IsCompleted INTEGER, DueDate TEXT,
                    Priority INTEGER, CategoryId TEXT, CreatedAt TEXT, UpdatedAt TEXT);
                INSERT INTO TaskItems VALUES ($id,'Legacy','Keep description',1,'2026-11-20',2,NULL,$time,$time);
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        }
        var task = Assert.Single(await new SqliteLocalTaskService(path).GetTasksAsync());
        Assert.Equal(id, task.Id); Assert.Equal("Keep description", task.Description); Assert.True(task.IsCompleted);
        Assert.Equal(TaskPriority.High, task.Priority); Assert.Equal(new DateOnly(2026,11,20), task.DueDate); Assert.Empty(task.Tags);
        using var check = new SqliteConnection($"Data Source={path};Pooling=False"); check.Open(); using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;"; Assert.Equal(7L, query.ExecuteScalar());
    }
    [Fact]
    public void LegacyMutationSerialization_OmitsNullTagsAndMatchesOldReceiptBytes()
    {
        const string legacy = """{"OperationId":"00000000-0000-0000-0000-000000000001","TaskId":"00000000-0000-0000-0000-000000000002","BaseRevision":0,"Deleted":false,"Task":{"Title":"Original","Description":null,"DueDate":null,"Priority":1,"CategoryName":null,"IsCompleted":false}}""";
        var mutation = JsonSerializer.Deserialize<SyncMutation>(legacy)!;
        Assert.Null(mutation.Task!.Tags); Assert.Equal(legacy, JsonSerializer.Serialize(mutation));
    }
    [Fact]
    public async Task CachedTagEquality_DoesNotQueueAnEndlessUploadAfterReopenAndAcknowledgement()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var id = Guid.NewGuid(); const string scope = "https://api.invalid/|one";
        await store.QueueAsync(scope, id, new("Tagged", Tags: Tags("Work")));
        var pending = (await store.ReadAsync(scope)).Rows[id].Pending!;
        await store.AcknowledgeAsync(scope, pending, new(false, new(id, 1, false, new("Tagged", Tags: Tags("work")))));
        Assert.Null((await store.ReadAsync(scope)).Rows[id].Pending);
        Assert.Equal(Tags("work"), (await store.ReadAsync(scope)).Rows[id].Local!.Tags);
    }
    [Fact]
    public async Task LegacyCacheAndViewSchemaUpgrades_DoNotRewriteOldPendingOrDefinitions()
    {
        Directory.CreateDirectory(directory); var id = Guid.NewGuid(); var operation = new SyncMutation(Guid.NewGuid(), id, 0, false, new("Legacy"));
        var state = new ReplicaState(); state.Rows[id] = new ReplicaRow { Id = id, Local = operation.Task, Pending = operation };
        var json = JsonSerializer.Serialize(state); var path = FileName("sync.db3");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE ReplicaPartitions (Partition TEXT PRIMARY KEY, State TEXT NOT NULL); INSERT INTO ReplicaPartitions VALUES ('legacy',$state); PRAGMA user_version=1;";
            command.Parameters.AddWithValue("$state", json); command.ExecuteNonQuery();
        }
        var pending = (await new SqliteSyncStore(path).ReadAsync("legacy")).Rows[id].Pending;
        Assert.Equal(JsonSerializer.Serialize(operation), JsonSerializer.Serialize(pending));
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT State FROM ReplicaPartitions;"; Assert.Equal(json, command.ExecuteScalar());
        }
        var views = new SqliteTaskViewStore(FileName("views.db3")); var saved = await views.CreateAsync("device", "Legacy", new());
        using (var connection = new SqliteConnection($"Data Source={FileName("views.db3")};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """UPDATE SavedTaskViews SET Definition='{"Search":"legacy"}'; PRAGMA user_version=1;"""; command.ExecuteNonQuery();
        }
        var restored = Assert.Single(await views.ReadAsync("device"));
        Assert.Equal(saved.Id, restored.Id); Assert.Equal("legacy", restored.Definition.Search); Assert.Equal(ViewTagMode.Any, restored.Definition.TagMode);
    }
    [Theory]
    [InlineData(ViewTagMode.AllListed, 1)]
    [InlineData(ViewTagMode.AnyListed, 2)]
    [InlineData(ViewTagMode.Untagged, 1)]
    public void TagFilters_MatchExactSetsAndCombineWithSearch(ViewTagMode mode, int count)
    {
        var rows = new[] { new TaskViewCandidate(Guid.NewGuid(), "One", null, false, null, TaskPriority.Normal, null, Tags("work, urgent")),
            new TaskViewCandidate(Guid.NewGuid(), "Two", null, false, null, TaskPriority.Normal, null, Tags("work")),
            new TaskViewCandidate(Guid.NewGuid(), "Three", null, false, null, TaskPriority.Normal, null) };
        Assert.Equal(count, TaskViewQueries.Apply(rows, new(TagMode: mode, Tags: Tags("work, urgent")), new(2026,11,13)).Count);
        Assert.Equal(2, TaskViewQueries.Apply(rows, new("work"), new(2026,11,13)).Count);
        Assert.Empty(TaskViewQueries.Apply(rows, new(TagMode: ViewTagMode.AnyListed, Tags: Tags("workshop")), new(2026,11,13)));
    }
    [Fact]
    public async Task TaggedSavedViews_ReopenWithRulesAndFilterCurrentTasks()
    {
        using var client = new ClientHarness(); var local = new InMemoryLocalTaskService();
        await local.CreateAsync(new TaskDetails("Tagged", Tags: Tags("work"))); await local.CreateAsync(new TaskDetails("Other"));
        var views = new SqliteTaskViewStore(FileName("views.db3")); var sync = new SqliteSyncStore(FileName("sync.db3"));
        using var vm = new TaskViewsViewModel(views, local, sync, client.Session, new ClientNavigation(), client.Clock);
        await vm.RefreshCommand.ExecuteAsync(); vm.TagMode = ViewTagMode.AllListed; vm.TagsInput = "WORK"; vm.ViewName = "Work";
        await vm.CreateCommand.ExecuteAsync(); Assert.Equal("Tagged", Assert.Single(vm.Results).Title);
        var definition = Assert.Single(await views.ReadAsync("device")).Definition; Assert.Equal(Tags("work"), definition.Tags);
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(TagMode: ViewTagMode.AllListed).Normalize());
    }
    [Fact]
    public async Task DeviceAndSyncedEditors_LoadSaveAndClearTagsExplicitly()
    {
        using var client = new ClientHarness(); var local = new InMemoryLocalTaskService(); var navigation = new ClientNavigation();
        var task = await local.CreateAsync(new TaskDetails("Edit", Tags: Tags("work")));
        var editor = new TaskEditorViewModel(local, navigation); await editor.LoadAsync(task.Id); Assert.Equal("work", editor.TagsInput);
        editor.TagsInput = ""; await editor.SaveCommand.ExecuteAsync(); Assert.Empty((await local.GetByIdAsync(task.Id))!.Tags);
        await client.LoginAsync(); var sync = new SqliteSyncStore(FileName("sync.db3"));
        using var vm = new SyncWorkspaceViewModel(client.Session, sync, new(client.Session, client.Api, sync, client.Clock), navigation);
        await vm.InitializeAsync(); vm.Title = "Cached"; vm.TagsInput = "Work, urgent"; await vm.SaveCommand.ExecuteAsync();
        var row = Assert.Single(vm.Tasks); Assert.Equal(Tags("work,urgent"), row.Local!.Tags);
        vm.SelectedTask = row; vm.TagsInput = ""; await vm.SaveCommand.ExecuteAsync();
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        Assert.Empty((await sync.ReadAsync(scope)).Rows[row.Id].Local!.Tags!);
        var frozen = (await sync.ReadAsync(scope)).Rows[row.Id].Pending!;
        Assert.Equal(Tags("work,urgent"), frozen.Task!.Tags);
        await sync.AcknowledgeAsync(scope, frozen, new(false, new(row.Id, 1, false, frozen.Task)));
        var clearing = (await sync.ReadAsync(scope)).Rows[row.Id].Pending!;
        Assert.NotEqual(frozen.OperationId, clearing.OperationId); Assert.Equal(1, clearing.BaseRevision);
        Assert.NotNull(clearing.Task!.Tags); Assert.Empty(clearing.Task.Tags);

    }
    [Fact]
    public async Task TagAwareUpload_RefusesOldApiAndRetainsTheSamePendingOperation()
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var store = new SqliteSyncStore(FileName("sync.db3")); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Queued", Tags: Tags("work"))); var pending = (await store.ReadAsync(scope)).Rows[id].Pending;
        client.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([], 0, false)));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(client.Session, client.Api, store, client.Clock).RunAsync());
        Assert.Equal(pending, (await store.ReadAsync(scope)).Rows[id].Pending);
        Assert.DoesNotContain(client.Handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/api/sync");
        Assert.Equal(DateTimeOffset.MaxValue, (await store.ReadAsync(scope)).Rows[id].RetryAt);
    }
}
