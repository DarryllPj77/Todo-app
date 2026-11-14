using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Reminders;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class RecurrenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_recurrence_" + Guid.NewGuid().ToString("N"));
    private string FileName(string name) => Path.Combine(directory, name);
    private static RepeatRule Rule(RepeatFrequency frequency = RepeatFrequency.Monthly, string anchor = "2026-01-31", int interval = 1) => new(frequency, interval, DateOnly.Parse(anchor));
    private static RepeatState Active(RepeatRule? rule = null) => new(rule ?? Rule(), 0, 0);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Theory]
    [InlineData(RepeatFrequency.Daily, "2026-01-31", 3, 2, "2026-02-06")]
    [InlineData(RepeatFrequency.Weekly, "2026-01-31", 2, 2, "2026-02-28")]
    [InlineData(RepeatFrequency.Monthly, "2026-01-31", 1, 1, "2026-02-28")]
    [InlineData(RepeatFrequency.Monthly, "2026-01-31", 1, 2, "2026-03-31")]
    [InlineData(RepeatFrequency.Monthly, "2024-01-31", 1, 1, "2024-02-29")]
    [InlineData(RepeatFrequency.Monthly, "2024-02-29", 12, 4, "2028-02-29")]
    public void Calendar_UsesAnchoredDatesWithoutMonthDrift(RepeatFrequency frequency, string anchor, int interval, long occurrence, string expected)
    {
        var state = new RepeatState(Rule(frequency, anchor, interval), occurrence, 0); RecurrenceRules.Validate(state);
        Assert.Equal(DateOnly.Parse(expected), RecurrenceRules.DueDate(state));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Rule\":null}")]
    [InlineData("{\"Rule\":{},\"Occurrence\":0,\"RuleStart\":0}")]
    [InlineData("{\"Rule\":{\"Frequency\":1,\"Interval\":1,\"AnchorDate\":\"2026-01-31\"},\"Occurrence\":0,\"RuleStart\":0}")]
    [InlineData("{\"Rule\":null,\"Occurrence\":0,\"RuleStart\":0,\"OwnerId\":1}")]
    public void RecurrenceJson_RequiresExplicitFieldsAndNamedFrequency(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RepeatState>(json));

    [Fact]
    public void RuleValidation_RejectsInvalidCountersBoundsAndCalendarOverflow()
    {
        foreach (var state in new[] { new RepeatState(null, -1, -1), new(null, 1, 0), new(Rule(), 0, 1), new(Rule(interval:0),0,0), new(Rule(interval:366),0,0), new(Rule((RepeatFrequency)99),0,0) })
            Assert.Throws<TaskRecurrenceException>(() => RecurrenceRules.Validate(state));
        var last = Active(Rule(RepeatFrequency.Daily, "9999-12-31"));
        Assert.Throws<TaskRecurrenceException>(() => RecurrenceRules.Complete(last, last));
        Assert.Throws<TaskRecurrenceException>(() => RecurrenceRules.ValidateWrite(Active(), Active() with { Occurrence = 2 }, new(2026,3,31), false, false));
    }
    [Fact]
    public void Completion_IsIdempotentAndRuleChangesDoNotResetConsumedCounter()
    {
        var first = Active(); var next = RecurrenceRules.Complete(first, first);
        Assert.Equal(next, RecurrenceRules.Complete(next, first));
        Assert.Equal(next, RecurrenceRules.Configure(next, first.Rule));
        var disabled = RecurrenceRules.Configure(next, null); Assert.Equal(1, disabled.Occurrence); Assert.Equal(disabled, RecurrenceRules.Complete(disabled, first));
        var configured = RecurrenceRules.Configure(disabled, Rule(RepeatFrequency.Weekly,"2026-04-02"));
        Assert.Equal(1, configured.RuleStart); Assert.Equal(new DateOnly(2026,4,2), RecurrenceRules.DueDate(configured));
        Assert.Throws<TaskRecurrenceException>(() => RecurrenceRules.Complete(configured, next));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Services_AdvanceOneStableTaskPreserveFieldsAndRejectOrdinaryCompletion(bool sqlite)
    {
        ILocalTaskService service = sqlite ? new SqliteLocalTaskService(FileName("tasks.db3")) : new InMemoryLocalTaskService();
        var parent = await service.CreateAsync(new("Project"));
        var task = await service.CreateAsync(new("Repeat", "Keep", Priority: TaskPriority.High, Tags: TagSet.Parse("work"), Parent: new(parent.Id), Quadrant: EisenhowerQuadrant.ImportantNotUrgent));
        task = (await service.SetRecurrenceRuleAsync(task.Id, Rule()))!; var expected = task.Recurrence; var created = task.CreatedAt;
        await Assert.ThrowsAsync<TaskRecurrenceException>(() => service.SetCompletedAsync(task.Id, true));
        await Assert.ThrowsAsync<TaskRecurrenceException>(() => service.UpdateAsync(task.Id, new("Wrong", DueDate: new(2026,2,1))));
        Assert.Equal("Repeat", (await service.GetByIdAsync(task.Id))!.Title);
        var results = await Task.WhenAll(service.CompleteOccurrenceAsync(task.Id, expected), service.CompleteOccurrenceAsync(task.Id, expected));
        Assert.All(results, r => Assert.Equal(1, r!.Recurrence.Occurrence));
        task = (await service.GetByIdAsync(task.Id))!; Assert.False(task.IsCompleted); Assert.Equal(new DateOnly(2026,2,28), task.DueDate); Assert.Equal(created, task.CreatedAt);
        Assert.Equal(parent.Id, task.ParentTaskId); Assert.Equal(TagSet.Parse("work"), task.Tags); Assert.Equal(TaskPriority.High, task.Priority); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, task.Quadrant);
        await service.CompleteOccurrenceAsync(task.Id, task.Recurrence); task = (await service.GetByIdAsync(task.Id))!; Assert.Equal(new DateOnly(2026,3,31), task.DueDate);
        await service.SetRecurrenceRuleAsync(task.Id, null); await service.SetCompletedAsync(task.Id, true);
        await Assert.ThrowsAsync<TaskRecurrenceException>(() => service.SetRecurrenceRuleAsync(task.Id, Rule()));
        Assert.Equal(2, (await service.GetTasksAsync()).Count);
        if (sqlite) Assert.Equal(2, (await new SqliteLocalTaskService(FileName("tasks.db3")).GetByIdAsync(task.Id))!.Recurrence.Occurrence);
    }
    [Fact]
    public async Task DependencyPolicy_RejectsRepeatingPrerequisitesAndChecksOwnPrerequisitesOnAdvance()
    {
        var service = new SqliteLocalTaskService(FileName("tasks.db3")); var first = await service.CreateAsync(new("First")); var second = await service.CreateAsync(new("Second", Prerequisites: TaskPrerequisites.FromIds([first.Id])));
        await Assert.ThrowsAsync<TodoApp.Domain.Relationships.TaskDependencyException>(() => service.SetRecurrenceRuleAsync(first.Id, Rule()));
        second = (await service.SetRecurrenceRuleAsync(second.Id, Rule()))!;
        await Assert.ThrowsAsync<TodoApp.Domain.Relationships.TaskDependencyException>(() => service.CompleteOccurrenceAsync(second.Id, second.Recurrence));
        await service.SetCompletedAsync(first.Id, true); await service.CompleteOccurrenceAsync(second.Id, second.Recurrence);
        await Assert.ThrowsAsync<TodoApp.Domain.Relationships.TaskDependencyException>(() => service.SetPrerequisitesAsync(first.Id, TaskPrerequisites.FromIds([second.Id])));
    }
    [Fact]
    public async Task VersionFiveSQLiteUpgrade_PreservesPlanningAndTimesAndAddsStoppedRule()
    {
        Directory.CreateDirectory(directory); var path = FileName("old.db3"); var id = Guid.NewGuid(); var time = DateTimeOffset.UtcNow;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
                CREATE TABLE TaskItems (Id TEXT PRIMARY KEY, Title TEXT, Description TEXT, IsCompleted INTEGER, DueDate TEXT, Priority INTEGER,
                    CategoryId TEXT, CreatedAt TEXT, UpdatedAt TEXT, Tags TEXT, ParentTaskId TEXT, Prerequisites TEXT, Quadrant INTEGER);
                INSERT INTO TaskItems VALUES ($id,'Legacy','Keep',1,'2026-11-20',2,NULL,$time,$time,'["work"]',NULL,'[]',2);
                PRAGMA user_version=5;
                """;
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$time", time.ToString("O")); command.ExecuteNonQuery();
        }
        var task = (await new SqliteLocalTaskService(path).GetByIdAsync(id))!; Assert.Equal(RepeatState.None, task.Recurrence); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, task.Quadrant);
        Assert.Equal(time, task.UpdatedAt); Assert.Equal(time, task.CreatedAt); Assert.True(task.IsCompleted); Assert.Equal(TagSet.Parse("work"), task.Tags);
    }
    [Fact]
    public async Task LegacyVersionFiveCache_UpgradesWithoutRewritingQueuedReceiptBytes()
    {
        Directory.CreateDirectory(directory); var path = FileName("sync.db3"); var id = Guid.NewGuid();
        var mutation = new SyncMutation(Guid.NewGuid(), id, 0, false, new("Old", Tags: TagSet.Parse("work"), Parent: new(null), Prerequisites: TaskPrerequisites.Empty, Quadrant: EisenhowerQuadrant.Neither));
        var state = new ReplicaState(); state.Rows[id] = new() { Id = id, Local = mutation.Task, Pending = mutation }; var json = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("Recurrence", JsonSerializer.Serialize(mutation));
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE ReplicaPartitions (Partition TEXT PRIMARY KEY, State TEXT NOT NULL); INSERT INTO ReplicaPartitions VALUES ('a',$json); PRAGMA user_version=5;";
            command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
        }
        Assert.Equal(JsonSerializer.Serialize(mutation), JsonSerializer.Serialize((await new SqliteSyncStore(path).ReadAsync("a")).Rows[id].Pending));
        using var check = new SqliteConnection($"Data Source={path};Pooling=False"); check.Open(); using var query = check.CreateCommand(); query.CommandText = "SELECT State FROM ReplicaPartitions";
        Assert.Equal(json, query.ExecuteScalar()); query.CommandText = "PRAGMA user_version"; Assert.Equal(7L, query.ExecuteScalar());
    }
    [Fact]
    public async Task CachedCompletion_RequiresSyncedOperationKeepsFrozenPayloadAndAdvancesOnceOffline()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var id = Guid.NewGuid(); var recurrence = Active();
        await store.QueueAsync("a", id, new("Task", DueDate: RecurrenceRules.DueDate(recurrence), Recurrence: recurrence)); var row = (await store.ReadAsync("a")).Rows[id];
        await Assert.ThrowsAsync<TaskRecurrenceException>(() => store.CompleteOccurrenceAsync("a", id, 0, row.Local!));
        var frozen = row.Pending!; await store.AcknowledgeAsync("a", frozen, new(false, new(id,1,false,frozen.Task)));
        row = (await store.ReadAsync("a")).Rows[id]; await store.CompleteOccurrenceAsync("a", id, 1, row.Local!); var advanced = (await store.ReadAsync("a")).Rows[id];
        Assert.Equal(1, advanced.Local!.Recurrence!.Occurrence); Assert.Equal(new DateOnly(2026,2,28), advanced.Local.DueDate);
        await Assert.ThrowsAsync<TaskRecurrenceException>(() => store.CompleteOccurrenceAsync("a", id, 1, advanced.Local));
        await store.QueueAsync("a", id, advanced.Local with { Title = "Later edit" }); Assert.Equal(advanced.Pending, (await store.ReadAsync("a")).Rows[id].Pending);
        await store.AcknowledgeAsync("a", advanced.Pending!, new(false, new(id,2,false,advanced.Pending!.Task,1))); var chained = (await store.ReadAsync("a")).Rows[id].Pending!;
        Assert.Equal(1, chained.Task!.Recurrence!.Occurrence); Assert.Equal("Later edit", chained.Task.Title);
    }
    [Fact]
    public async Task InvalidRemoteCounters_DoNotAcknowledgeOrAdvanceTheCursor()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var id = Guid.NewGuid();
        await store.QueueAsync("a", id, new("Task")); var pending = (await store.ReadAsync("a")).Rows[id].Pending!;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcknowledgeAsync("a", pending, new(false, new(id,1,false,pending.Task,1))));
        Assert.Equal(pending, (await store.ReadAsync("a")).Rows[id].Pending);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ApplyPageAsync("a",0,new([new(id,1,false,pending.Task,1)],1,false,6)));
        Assert.Equal(0, (await store.ReadAsync("a")).Cursor);
    }
    [Fact]
    public async Task ProtocolFiveApi_BlocksRecurrenceBeforeUploadAndKeepsOperation()
    {
        using var client = new ClientHarness(); await client.LoginAsync(); var store = new SqliteSyncStore(FileName("sync.db3")); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id); var id = Guid.NewGuid();
        await store.QueueAsync(scope, id, new("Queued", Recurrence: RepeatState.None)); var pending = (await store.ReadAsync(scope)).Rows[id].Pending;
        client.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([],0,false,5)));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(client.Session,client.Api,store,client.Clock).RunAsync());
        Assert.Equal(pending,(await store.ReadAsync(scope)).Rows[id].Pending); Assert.Equal(DateTimeOffset.MaxValue,(await store.ReadAsync(scope)).Rows[id].RetryAt);
        Assert.DoesNotContain(client.Handler.Requests,r=>r.Method==HttpMethod.Post&&r.Uri.AbsolutePath=="/api/sync");
    }
    [Fact]
    public async Task ConflictRepair_RebasesToRemoteCounterIncludingDeletedTask()
    {
        var store = new SqliteSyncStore(FileName("sync.db3")); var id = Guid.NewGuid(); var recurrence = Active();
        await store.QueueAsync("a",id,new("Local",DueDate:RecurrenceRules.DueDate(recurrence),Recurrence:recurrence)); var pending=(await store.ReadAsync("a")).Rows[id].Pending!;
        await store.AcknowledgeAsync("a",pending,new(true,new(id,3,true,null,2))); var row=(await store.ReadAsync("a")).Rows[id];
        await Assert.ThrowsAsync<TaskRecurrenceException>(()=>store.SetRecurrenceRuleAsync("a",id,Rule(),3,row.Local!));
        await store.SetRecurrenceRuleAsync("a",id,Rule(),3,row.Local!,true); row=(await store.ReadAsync("a")).Rows[id];
        Assert.False(row.Conflict); Assert.Equal(2,row.Local!.Recurrence!.Occurrence); Assert.Equal(2,row.Local.Recurrence.RuleStart); Assert.Equal("Local",row.Local.Title);
        Assert.Equal(3,row.Pending!.BaseRevision);
    }
    [Fact]
    public async Task RecurrencePage_SavesPreviewsAndRequiresConfirmationToAdvance()
    {
        using var client=new ClientHarness(); var service=new InMemoryLocalTaskService(); var store=new SqliteSyncStore(FileName("sync.db3")); var navigation=new ClientNavigation(); var task=await service.CreateAsync(new("Task"));
        using var vm=new RecurrenceViewModel(service,store,client.Session,navigation); await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask=Assert.Single(vm.Tasks);
        vm.Frequency=vm.Frequencies.Single(f=>f.Value==RepeatFrequency.Monthly); vm.AnchorDate=new DateTime(2026,1,31); vm.Interval="1";
        Assert.Contains("2026-01-31",vm.Preview); await vm.SaveCommand.ExecuteAsync(); vm.SelectedTask=Assert.Single(vm.Tasks);
        await vm.CompleteCommand.ExecuteAsync(); Assert.Equal(0,(await service.GetByIdAsync(task.Id))!.Recurrence.Occurrence);
        navigation.Confirm=true; await vm.CompleteCommand.ExecuteAsync(); Assert.Equal(new DateOnly(2026,2,28),(await service.GetByIdAsync(task.Id))!.DueDate);
        vm.SelectedTask=Assert.Single(vm.Tasks); vm.Interval="0"; await vm.SaveCommand.ExecuteAsync(); Assert.NotEmpty(vm.Error); Assert.Equal(1,(await service.GetByIdAsync(task.Id))!.Recurrence.Occurrence);
    }
    [Fact]
    public async Task AccountChangeDuringOccurrenceConfirmation_RetainsTheOldAccountCache()
    {
        using var client=new ClientHarness(); await client.LoginAsync(); var store=new SqliteSyncStore(FileName("sync.db3")); var scope=SqliteSyncStore.Partition(client.Session.Current!.Origin,client.Account.User.Id); var id=Guid.NewGuid(); var repeat=Active();
        await store.ApplyPageAsync(scope,0,new([new(id,1,false,new("Private",DueDate:RecurrenceRules.DueDate(repeat),Recurrence:repeat))],1,false,6));
        var navigation=new ClientNavigation { OccurrenceConfirmation=async()=>{await client.Session.SignOutAsync();return true;} };
        using var vm=new RecurrenceViewModel(new InMemoryLocalTaskService(),store,client.Session,navigation){Workspace=HierarchyWorkspace.SyncedAccount}; await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask=Assert.Single(vm.Tasks);
        await vm.CompleteCommand.ExecuteAsync(); Assert.Empty(vm.Tasks); Assert.Null(vm.SelectedTask); Assert.Equal(0,(await store.ReadAsync(scope)).Rows[id].Local!.Recurrence!.Occurrence); Assert.Null((await store.ReadAsync(scope)).Rows[id].Pending);
    }
    [Fact]
    public async Task AdvancingOccurrence_CancelsOldReminderWithoutSchedulingAnother()
    {
        using var client=new ClientHarness(); var tasks=new SqliteLocalTaskService(FileName("tasks.db3")); var sync=new SqliteSyncStore(FileName("sync.db3")); var reminders=new SqliteReminderStore(FileName("reminders.db3")); var platform=new FakeReminderPlatform();
        var service=new ReminderService(reminders,new(tasks,sync,client.Session),platform,client.Session,client.Clock); var decorated=new ReminderAwareTaskService(tasks,service);
        var task=await decorated.CreateAsync(new("Repeat")); task=(await decorated.SetRecurrenceRuleAsync(task.Id,Rule(RepeatFrequency.Daily,"2026-11-13")))!;
        await service.SaveAsync(new("device",task.Id,task.Title,task.DueDate,false),client.Clock.Now.UtcDateTime.Date,TimeSpan.FromHours(1),TimeZoneInfo.Utc); Assert.Single(platform.Scheduled);
        await decorated.CompleteOccurrenceAsync(task.Id,task.Recurrence); Assert.Empty(platform.Scheduled); Assert.False(Assert.Single(await reminders.ReadAsync()).Enabled);
    }
}
