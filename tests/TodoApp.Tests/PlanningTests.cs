using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Domain.Views;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class PlanningTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_planning_" + Guid.NewGuid().ToString("N"));
    private string FileName(string name) => Path.Combine(directory, name);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    [Theory]
    [InlineData(true, true, EisenhowerQuadrant.UrgentImportant)]
    [InlineData(false, true, EisenhowerQuadrant.ImportantNotUrgent)]
    [InlineData(true, false, EisenhowerQuadrant.UrgentNotImportant)]
    [InlineData(false, false, EisenhowerQuadrant.Neither)]
    public void BothAxes_MapToDistinctQuadrants(bool urgent, bool important, EisenhowerQuadrant expected)
    {
        Assert.Equal(expected, Eisenhower.FromFlags(urgent, important)); Assert.Equal(urgent, Eisenhower.IsUrgent(expected)); Assert.Equal(important, Eisenhower.IsImportant(expected));
        Assert.Equal(expected, JsonSerializer.Deserialize<EisenhowerQuadrant>(JsonSerializer.Serialize(expected)));
    }
    [Theory]
    [InlineData("0")]
    [InlineData("\"0\"")]
    [InlineData("\"future\"")]
    [InlineData("null")]
    public void InvalidWireQuadrants_AreRejected(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EisenhowerQuadrant>(json));
    [Fact]
    public void Unclassified_IsDistinctFromNeitherAndDoesNotFollowPriorityOrDate()
    {
        var task = new TaskItem("Overdue", dueDate: new(2020,1,1), priority: TaskPriority.High); var before = task.UpdatedAt;
        Assert.Equal(EisenhowerQuadrant.Unclassified, task.Quadrant); Assert.Null(Eisenhower.IsUrgent(task.Quadrant)); Assert.Null(Eisenhower.IsImportant(task.Quadrant));
        task.SetQuadrant(EisenhowerQuadrant.Unclassified); Assert.Equal(before, task.UpdatedAt);
        Assert.Throws<ArgumentException>(() => task.SetQuadrant((EisenhowerQuadrant)99)); Assert.Equal(before, task.UpdatedAt);
        task.SetQuadrant(EisenhowerQuadrant.Neither); Assert.False(Eisenhower.IsUrgent(task.Quadrant)); Assert.False(Eisenhower.IsImportant(task.Quadrant));
        Assert.Equal(TaskPriority.High, task.Priority); Assert.Equal(new DateOnly(2020,1,1), task.DueDate);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Services_PreserveOmittedPlanningClearExplicitlyAndKeepUnrelatedFields(bool sqlite)
    {
        ILocalTaskService service = sqlite ? new SqliteLocalTaskService(FileName("tasks.db3")) : new InMemoryLocalTaskService();
        var prerequisite = await service.CreateAsync(new("First"));
        var task = await service.CreateAsync(new("Second", "Keep", new(2026,11,20), TaskPriority.High, Tags: TagSet.Parse("work"), Parent: new(prerequisite.Id),
            Prerequisites: TaskPrerequisites.FromIds([prerequisite.Id]), Quadrant: EisenhowerQuadrant.UrgentImportant));
        await service.UpdateAsync(task.Id, new("Edited", "Keep", task.DueDate, task.Priority));
        Assert.Equal(EisenhowerQuadrant.UrgentImportant, (await service.GetByIdAsync(task.Id))!.Quadrant);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(task.Id, new("Wrong", Quadrant: (EisenhowerQuadrant)99)));
        Assert.Equal("Edited", (await service.GetByIdAsync(task.Id))!.Title);
        await service.SetQuadrantAsync(task.Id, EisenhowerQuadrant.Unclassified); var saved = (await service.GetByIdAsync(task.Id))!;
        Assert.Equal(EisenhowerQuadrant.Unclassified, saved.Quadrant); Assert.Equal(task.ParentTaskId, saved.ParentTaskId); Assert.Equal(task.Prerequisites, saved.Prerequisites);
        Assert.Equal(task.Tags, saved.Tags); Assert.Equal(task.CreatedAt, saved.CreatedAt); Assert.Equal(task.DueDate, saved.DueDate); Assert.Equal(task.Priority, saved.Priority);
        await service.SetQuadrantAsync(task.Id, EisenhowerQuadrant.ImportantNotUrgent);
        if (sqlite) Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, (await new SqliteLocalTaskService(FileName("tasks.db3")).GetByIdAsync(task.Id))!.Quadrant);
    }
    [Fact]
    public async Task VersionFourUpgrade_PreservesFieldsAndIntroducesUnclassifiedWithConstraint()
    {
        Directory.CreateDirectory(directory); var path = FileName("old.db3"); var id = Guid.NewGuid(); var time = DateTimeOffset.UtcNow;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
                CREATE TABLE TaskItems (Id TEXT PRIMARY KEY, Title TEXT, Description TEXT, IsCompleted INTEGER, DueDate TEXT, Priority INTEGER,
                    CategoryId TEXT, CreatedAt TEXT, UpdatedAt TEXT, Tags TEXT, ParentTaskId TEXT, Prerequisites TEXT);
                INSERT INTO TaskItems VALUES ($id,'Legacy','Keep',1,'2026-11-20',2,NULL,$time,$time,'["work"]',NULL,'[]');
                PRAGMA user_version=4;
                """;
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$time", time.ToString("O")); command.ExecuteNonQuery();
        }
        var restored = (await new SqliteLocalTaskService(path).GetByIdAsync(id))!; Assert.Equal(EisenhowerQuadrant.Unclassified, restored.Quadrant);
        Assert.Equal(time, restored.UpdatedAt); Assert.Equal(time, restored.CreatedAt); Assert.Equal(TagSet.Parse("work"), restored.Tags); Assert.True(restored.IsCompleted);
        using var check = new SqliteConnection($"Data Source={path};Pooling=False"); check.Open(); using var query = check.CreateCommand();
        query.CommandText = "UPDATE TaskItems SET Quadrant=99"; Assert.Throws<SqliteException>(() => query.ExecuteNonQuery());
        query.CommandText = "PRAGMA user_version"; Assert.Equal(5L, query.ExecuteScalar());
    }
    [Fact]
    public async Task LegacyOperationAndDefinitionBytes_SurviveFormatUpgradesWithoutRewriteOnRead()
    {
        Directory.CreateDirectory(directory); var id = Guid.NewGuid(); var operation = new SyncMutation(Guid.NewGuid(), id, 0, false,
            new("Legacy", Tags: TagSet.Parse("work"), Parent: new(null), Prerequisites: TaskPrerequisites.Empty));
        var bytes = JsonSerializer.Serialize(operation); Assert.DoesNotContain("Quadrant", bytes);
        var state = new ReplicaState(); state.Rows[id] = new() { Id = id, Local = operation.Task, Pending = operation }; var json = JsonSerializer.Serialize(state);
        var path = FileName("sync.db3");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE ReplicaPartitions (Partition TEXT PRIMARY KEY, State TEXT NOT NULL); INSERT INTO ReplicaPartitions VALUES ('a',$json); PRAGMA user_version=4;";
            command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
        }
        Assert.Equal(bytes, JsonSerializer.Serialize((await new SqliteSyncStore(path).ReadAsync("a")).Rows[id].Pending));
        using (var check = new SqliteConnection($"Data Source={path};Pooling=False"))
        { check.Open(); using var query = check.CreateCommand(); query.CommandText = "SELECT State FROM ReplicaPartitions"; Assert.Equal(json, query.ExecuteScalar()); }
        var views = new SqliteTaskViewStore(FileName("views.db3")); await views.CreateAsync("device", "Legacy", new());
        using (var connection = new SqliteConnection($"Data Source={FileName("views.db3")};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """UPDATE SavedTaskViews SET Definition='{"Search":"keep"}'; PRAGMA user_version=2;"""; command.ExecuteNonQuery(); }
        Assert.Null(Assert.Single(await views.ReadAsync("device")).Definition.Quadrant);
        using var viewCheck = new SqliteConnection($"Data Source={FileName("views.db3")};Pooling=False"); viewCheck.Open(); using var viewQuery = viewCheck.CreateCommand();
        viewQuery.CommandText = "SELECT Definition FROM SavedTaskViews"; Assert.Equal("{\"Search\":\"keep\"}", viewQuery.ExecuteScalar());
        viewQuery.CommandText = "PRAGMA user_version"; Assert.Equal(3L, viewQuery.ExecuteScalar());
    }
    [Fact]
    public async Task CachedPlanning_PreservesFrozenOperationChainsLaterEditsAndGuardsRepair()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var id = Guid.NewGuid();
        await store.QueueAsync("a", id, new("First", Quadrant: EisenhowerQuadrant.UrgentImportant)); var frozen = (await store.ReadAsync("a")).Rows[id].Pending!;
        await store.QueueAsync("a", id, new("Edited")); var row = (await store.ReadAsync("a")).Rows[id];
        await store.SetQuadrantAsync("a", id, EisenhowerQuadrant.Neither, 0, row.Local!); Assert.Equal(frozen, (await store.ReadAsync("a")).Rows[id].Pending);
        await store.AcknowledgeAsync("a", frozen, new(false, new(id, 1, false, frozen.Task))); var next = (await store.ReadAsync("a")).Rows[id].Pending!;
        Assert.Equal(EisenhowerQuadrant.Neither, next.Task!.Quadrant); Assert.Equal("Edited", next.Task.Title); Assert.NotEqual(frozen.OperationId, next.OperationId);
        await store.AcknowledgeAsync("a", next, new(true, new(id, 2, false, new("Remote", Quadrant: EisenhowerQuadrant.ImportantNotUrgent)))); row = (await store.ReadAsync("a")).Rows[id];
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetQuadrantAsync("a", id, EisenhowerQuadrant.Unclassified, 1, row.Local!, true));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetQuadrantAsync("a", id, EisenhowerQuadrant.Unclassified, 2, row.Local!));
        await store.SetQuadrantAsync("a", id, EisenhowerQuadrant.Unclassified, 2, row.Local!, true); var repaired = (await store.ReadAsync("a")).Rows[id];
        Assert.False(repaired.Conflict); Assert.Equal("Edited", repaired.Local!.Title); Assert.Equal(EisenhowerQuadrant.Unclassified, repaired.Pending!.Task!.Quadrant); Assert.Equal(2, repaired.Pending.BaseRevision);
    }
    [Fact]
    public async Task ProtocolFourApi_RejectsPlanningBeforeUploadAndRetainsOriginalOperation()
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Queued", Quadrant: EisenhowerQuadrant.Unclassified)); var pending = (await store.ReadAsync(scope)).Rows[id].Pending;
        client.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([], 0, false, 4)));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(client.Session, client.Api, store, client.Clock).RunAsync());
        Assert.Equal(pending, (await store.ReadAsync(scope)).Rows[id].Pending); Assert.Equal(DateTimeOffset.MaxValue, (await store.ReadAsync(scope)).Rows[id].RetryAt);
        Assert.DoesNotContain(client.Handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/api/sync");
    }
    [Fact]
    public async Task PlanningScreen_FiltersPendingTasksSavesAndClearsWithoutChangingTaskDetails()
    {
        using var client = new ClientHarness(); var service = new InMemoryLocalTaskService(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var task = await service.CreateAsync(new("Keep", Priority: TaskPriority.High, Tags: TagSet.Parse("work"))); var completed = await service.CreateAsync(new("Done")); await service.SetCompletedAsync(completed.Id, true);
        using var vm = new PlanningViewModel(service, store, client.Session, new ClientNavigation()); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks); vm.SelectedQuadrant = vm.Quadrants.Single(q => q.Value == EisenhowerQuadrant.ImportantNotUrgent);
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, (await service.GetByIdAsync(task.Id))!.Quadrant); Assert.Equal(TaskPriority.High, task.Priority);
        vm.Filter = vm.Filters.Single(q => q.Value == EisenhowerQuadrant.Unclassified); Assert.Empty(vm.Tasks);
        vm.IncludeCompleted = true; Assert.Equal(completed.Id, Assert.Single(vm.Tasks).Id);
        vm.Filter = vm.Filters[0]; vm.SelectedTask = vm.Tasks.Single(t => t.Id == task.Id); vm.SelectedQuadrant = vm.Quadrants.Single(q => q.Value == EisenhowerQuadrant.Unclassified);
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(EisenhowerQuadrant.Unclassified, (await service.GetByIdAsync(task.Id))!.Quadrant); Assert.Equal(TagSet.Parse("work"), task.Tags);
    }
    [Fact]
    public async Task PlanningConflict_RequiresConfirmationAndAccountSwitchRejectsOldAction()
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(FileName("sync.db3"));
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Local", Quadrant: EisenhowerQuadrant.Neither)); var pending = (await store.ReadAsync(scope)).Rows[id].Pending!;
        await store.AcknowledgeAsync(scope, pending, new(true, new(id, 1, false, new("Remote", Quadrant: EisenhowerQuadrant.UrgentImportant))));
        var navigation = new ClientNavigation(); using var vm = new PlanningViewModel(new InMemoryLocalTaskService(), store, client.Session, navigation) { Workspace = HierarchyWorkspace.SyncedAccount };
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks); await vm.KeepLocalCommand.ExecuteAsync(); Assert.True((await store.ReadAsync(scope)).Rows[id].Conflict);
        navigation.Confirm = true; await vm.KeepLocalCommand.ExecuteAsync(); var repaired = (await store.ReadAsync(scope)).Rows[id]; Assert.False(repaired.Conflict); Assert.Equal("Local", repaired.Local!.Title);
        await store.AcknowledgeAsync(scope, repaired.Pending!, new(true, new(id, 2, false, new("Remote")))); await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks);
        navigation.PlanningConfirmation = async () => { await client.Session.SignOutAsync(); return true; }; await vm.KeepLocalCommand.ExecuteAsync();
        Assert.Empty(vm.Tasks); Assert.Empty(vm.Counts); Assert.Null(vm.SelectedTask); Assert.True((await store.ReadAsync(scope)).Rows[id].Conflict);
    }
    [Fact]
    public async Task SavedViews_CombineAndRetainQuadrantRulesAcrossReopen()
    {
        using var client = new ClientHarness(); var local = new InMemoryLocalTaskService(); var sync = new SqliteSyncStore(FileName("sync.db3")); var views = new SqliteTaskViewStore(FileName("views.db3"));
        await local.CreateAsync(new("Focus", Priority: TaskPriority.High, Quadrant: EisenhowerQuadrant.ImportantNotUrgent)); await local.CreateAsync(new("Other", Priority: TaskPriority.High));
        using var vm = new TaskViewsViewModel(views, local, sync, client.Session, new ClientNavigation(), client.Clock); await vm.RefreshCommand.ExecuteAsync();
        vm.Quadrant = EisenhowerQuadrant.ImportantNotUrgent; vm.Priority = ViewPriority.High; vm.ViewName = "Schedule"; await vm.CreateCommand.ExecuteAsync(); Assert.Equal("Focus", Assert.Single(vm.Results).Title);
        var definition = Assert.Single(await views.ReadAsync("device")).Definition; Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, definition.Quadrant);
        using var reopened = new TaskViewsViewModel(views, local, sync, client.Session, new ClientNavigation(), client.Clock); await reopened.RefreshCommand.ExecuteAsync(); reopened.SelectedView = Assert.Single(reopened.SavedViews);
        await reopened.ApplyCommand.ExecuteAsync(); Assert.Single(reopened.Results);
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(Quadrant: (EisenhowerQuadrant)99).Normalize());
    }
}
