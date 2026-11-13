using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Focus;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

internal sealed class FocusClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026,11,14,0,0,0,TimeSpan.Zero);
    public long Stamp { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Stamp;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(double seconds) { Stamp += TimeSpan.FromSeconds(seconds).Ticks; Now = Now.AddSeconds(seconds); }
}

public sealed class FocusTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),"todoapp_focus_"+Guid.NewGuid().ToString("N"));
    private readonly List<FocusService> services = new();
    private readonly FocusClock clock = new();
    private readonly InMemoryLocalTaskService tasks = new();
    private readonly MemoryClientSettings settings = new();
    private readonly ClientSession session;
    private string PathFor(string name) => Path.Combine(directory,name);
    private SqliteFocusStore Store => new(PathFor("focus.db3"));
    private SqliteSyncStore Sync => new(PathFor("sync.db3"));
    private FocusTaskCatalog Catalog => new(tasks,Sync,session,clock);
    public FocusTests() => session = new(settings,new MemoryTokenStore(),clock);
    private FocusService Service(IFocusTaskSource? source = null)
    { var value = new FocusService(Store,source ?? Catalog,session,clock);services.Add(value);return value; }
    public void Dispose() { foreach(var service in services)service.Dispose(); if(Directory.Exists(directory))Directory.Delete(directory,true); }
    private async Task<Guid> TaskAsync(string title="Task") => (await tasks.CreateAsync(new(title))).Id;
    private async Task<string> LoginAsync(string email="alice@example.invalid")
    {
        var user=new ApiUser(Guid.NewGuid(),email);await session.SaveAsync(new("fake-token","Bearer",clock.Now.AddMinutes(15),user),session.Endpoint,session.Version);
        return SqliteSyncStore.Partition(session.Current!.Origin,user.Id);
    }
    private async Task Tick(FocusService service,double seconds=5) {clock.Advance(seconds);await service.TickSafelyAsync();}
    private async Task FinishMinute(FocusService service) {for(var i=0;i<12;i++)await Tick(service);}
    private static FocusPreferences Minute => new(1,1,1);
    private void Sql(string sql)
    { using var connection=new SqliteConnection($"Data Source={PathFor("focus.db3")};Pooling=False");connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery(); }

    [Theory]
    [InlineData(0,5,15)]
    [InlineData(181,5,15)]
    [InlineData(25,0,15)]
    [InlineData(25,61,15)]
    [InlineData(25,5,0)]
    [InlineData(25,5,61)]
    public void Durations_RejectInvalidBounds(int work,int shortBreak,int longBreak) => Assert.Throws<ArgumentException>(()=>new FocusPreferences(work,shortBreak,longBreak).Validate());

    [Fact]
    public async Task Completion_IsSavedOnceAndDoesNotCompleteTheTask()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await FinishMinute(focus);
        var snapshot=await focus.ReadAsync("device");Assert.Null(snapshot.Active);var entry=Assert.Single(snapshot.History);
        Assert.Equal(FocusStatus.Completed,entry.Session.Status);Assert.Equal(FocusEndReason.TargetReached,entry.Session.EndReason);Assert.Equal(TimeSpan.TicksPerMinute,FocusRules.TimeSpentTicks(snapshot.History));
        await focus.StopAsync("device",entry.Session.Id);await Tick(focus);Assert.Single((await focus.ReadAsync("device")).History);Assert.False((await tasks.GetByIdAsync(id))!.IsCompleted);
    }
    [Fact]
    public async Task PauseResume_ExcludesPausedTimeAndStopRetainsOneLedgerEntry()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);var active=(await focus.ReadAsync("device")).Active!;
        await focus.PauseAsync("device",active.Session.Id);await Tick(focus,300);Assert.Equal(TimeSpan.FromSeconds(5).Ticks,(await focus.ReadAsync("device")).Active!.Session.ElapsedTicks);
        await focus.ResumeAsync("device",active.Session.Id);await Tick(focus);await focus.StopAsync("device",active.Session.Id);var entry=Assert.Single((await focus.ReadAsync("device")).History);
        Assert.Equal(FocusStatus.Stopped,entry.Session.Status);Assert.Equal(TimeSpan.FromSeconds(10).Ticks,entry.Session.ElapsedTicks);Assert.Null((await focus.ReadAsync("device")).Active);
    }
    [Theory]
    [InlineData(FocusKind.ShortBreak)]
    [InlineData(FocusKind.LongBreak)]
    public async Task Breaks_DoNotAddToTimeSpent(FocusKind kind)
    {
        var focus=Service();await focus.StartAsync("device",kind,null,Minute);await FinishMinute(focus);var snapshot=await focus.ReadAsync("device");
        Assert.Equal(0,FocusRules.TimeSpentTicks(snapshot.History));Assert.Null(Assert.Single(snapshot.History).Session.TaskId);Assert.Equal(FocusStatus.Completed,snapshot.History[0].Session.Status);
    }
    [Fact]
    public async Task WallClockChanges_DoNotChangeMonotonicCredit()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);clock.Now=clock.Now.AddHours(-2);await Tick(focus);
        var snapshot=await focus.ReadAsync("device");Assert.Equal(TimeSpan.FromSeconds(10).Ticks,snapshot.Active!.Session.ElapsedTicks);Assert.True(snapshot.Active.Session.UpdatedAtUtc>=snapshot.Active.Session.StartedAtUtc);
        clock.Now=clock.Now.AddHours(8);await Tick(focus);Assert.Equal(TimeSpan.FromSeconds(15).Ticks,(await focus.ReadAsync("device")).Active!.Session.ElapsedTicks);
    }
    [Fact]
    public async Task LongHeartbeatGap_InterruptsWithoutCreditingTheGap()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);await Tick(focus,40);var snapshot=await focus.ReadAsync("device");
        Assert.Null(snapshot.Active);var row=Assert.Single(snapshot.History);Assert.Equal(FocusEndReason.TimingGap,row.Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(5).Ticks,row.Session.ElapsedTicks);
    }
    [Fact]
    public async Task Restart_RecoversOnlyTheLastSavedCheckpointAndNeverAutoResumes()
    {
        var id=await TaskAsync();var first=Service();await first.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(first);await Tick(first,2);Assert.Equal(TimeSpan.FromSeconds(7).Ticks,(await first.ReadAsync("device")).Active!.Session.ElapsedTicks);first.Dispose();
        clock.Advance(3600);var second=Service();var recovered=await second.ReadAsync("device");var row=Assert.Single(recovered.History);Assert.Null(recovered.Active);Assert.Equal(FocusEndReason.AppRestarted,row.Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(5).Ticks,row.Session.ElapsedTicks);
        var revision=row.Revision;await Tick(second);Assert.Equal(revision,Assert.Single((await second.ReadAsync("device")).History).Revision);
    }
    [Fact]
    public async Task PausedSession_RestartIsTerminalAndSuspensionRetainsObservedTime()
    {
        var id=await TaskAsync();var first=Service();await first.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(first);var current=(await first.ReadAsync("device")).Active!;await first.PauseAsync("device",current.Session.Id);first.Dispose();
        var second=Service();Assert.Null((await second.ReadAsync("device")).Active);await second.StartAsync("device",FocusKind.Focus,id,Minute);clock.Advance(3);await second.SuspendAsync();
        var rows=(await second.ReadAsync("device")).History;Assert.Equal(2,rows.Count);Assert.Equal(FocusEndReason.AppSuspended,rows[0].Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(8).Ticks,FocusRules.TimeSpentTicks(rows));
    }
    [Fact]
    public async Task CompletingOrDeletingTask_InterruptsAtLastVerifiedCheckpoint()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);await Tick(focus,3);await tasks.SetCompletedAsync(id,true);await Tick(focus,2);
        var row=Assert.Single((await focus.ReadAsync("device")).History);Assert.Equal(FocusEndReason.TaskUnavailable,row.Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(5).Ticks,row.Session.ElapsedTicks);
        await tasks.SetCompletedAsync(id,false);await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);await tasks.DeleteAsync(id);await Tick(focus);
        Assert.Equal(2,(await focus.ReadAsync("device")).History.Count);Assert.Equal(TimeSpan.FromSeconds(10).Ticks,FocusRules.TimeSpentTicks((await focus.ReadAsync("device")).History));
    }
    [Fact]
    public async Task RecurrenceChange_InterruptsWhileOrdinaryRenameKeepsTheSnapshotTitle()
    {
        var id=await TaskAsync("Old title");var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);await tasks.UpdateAsync(id,new("New title"));await Tick(focus);
        Assert.Equal("Old title",(await focus.ReadAsync("device")).Active!.Session.Title);
        await tasks.SetRecurrenceRuleAsync(id,new(RepeatFrequency.Daily,1,new(2026,11,14)));await Tick(focus);var row=Assert.Single((await focus.ReadAsync("device")).History);
        Assert.Equal(FocusEndReason.OccurrenceChanged,row.Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(10).Ticks,row.Session.ElapsedTicks);
    }
    [Fact]
    public async Task AccountSwitch_ClearsVisibleHistoryAndDoesNotCreditAnotherAccount()
    {
        var scope=await LoginAsync();var id=Guid.NewGuid();await Sync.QueueAsync(scope,id,new("Private title"));var focus=Service();using var vm=new FocusViewModel(focus,Catalog,session){Workspace=HierarchyWorkspace.SyncedAccount};await vm.RefreshCommand.ExecuteAsync();vm.SelectedTask=Assert.Single(vm.Tasks);await vm.StartFocusCommand.ExecuteAsync();await Tick(focus);
        await session.SignOutAsync();Assert.Empty(vm.Tasks);Assert.Empty(vm.History);Assert.Empty(vm.TaskTotals);Assert.Null(vm.Active);
        await focus.ReadAsync("device");var old=Assert.Single(await Store.ReadAsync(scope));Assert.Equal(FocusEndReason.AccountChanged,old.Session.EndReason);Assert.Equal(TimeSpan.FromSeconds(5).Ticks,old.Session.ElapsedTicks);
        var bob=await LoginAsync("bob@example.invalid");await vm.RefreshCommand.ExecuteAsync();Assert.Empty(vm.History);Assert.Empty(vm.TaskTotals);await Assert.ThrowsAsync<ApiClientException>(()=>focus.ReadAsync(scope));Assert.Empty((await focus.ReadAsync(bob)).History);
    }
    [Fact]
    public async Task Expiry_StopsAccountTrackingAndPollClearsSensitiveRows()
    {
        var scope=await LoginAsync();var id=Guid.NewGuid();await Sync.QueueAsync(scope,id,new("Private"));var focus=Service();using var vm=new FocusViewModel(focus,Catalog,session){Workspace=HierarchyWorkspace.SyncedAccount};await vm.RefreshCommand.ExecuteAsync();vm.SelectedTask=Assert.Single(vm.Tasks);await vm.StartFocusCommand.ExecuteAsync();
        await Tick(focus,16*60);await vm.PollAsync();Assert.Empty(vm.Tasks);Assert.Empty(vm.History);Assert.Null(vm.Active);Assert.Equal(0,Assert.Single(await Store.ReadAsync(scope)).Session.ElapsedTicks);
    }
    [Fact]
    public async Task DeviceSession_SurvivesAccountLoginAndForeignScopeNeverShowsItsTitle()
    {
        var id=await TaskAsync("Device secret");var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);var scope=await LoginAsync();await Tick(focus);
        var account=await focus.ReadAsync(scope);Assert.True(account.OtherScopeActive);Assert.Null(account.Active);Assert.Empty(account.History);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>focus.StartAsync(scope,FocusKind.ShortBreak,null,Minute));Assert.NotNull((await focus.ReadAsync("device")).Active);
    }
    [Fact]
    public async Task TwoAppInstances_OnlyTheLeaseOwnerCanWriteAndTakeoverRecovers()
    {
        var id=await TaskAsync();var first=Service();await first.StartAsync("device",FocusKind.Focus,id,Minute);var second=Service();var snapshot=await second.ReadAsync("device");Assert.False(snapshot.CanControl);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>second.StartAsync("device",FocusKind.Focus,id,Minute));first.Dispose();snapshot=await second.ReadAsync("device");Assert.True(snapshot.CanControl);Assert.Null(snapshot.Active);Assert.Equal(FocusEndReason.AppRestarted,Assert.Single(snapshot.History).Session.EndReason);
    }
    [Fact]
    public async Task ConcurrentStartsAcrossStoreInstances_SaveOnlyOneSessionAndItsSettings()
    {
        var task=new FocusTask("device",Guid.NewGuid(),"Task",RepeatState.None);var one=FocusRules.Start("device",task,FocusKind.Focus,Minute,clock.Now);var two=FocusRules.Start("other",null,FocusKind.ShortBreak,new(2,2,2),clock.Now);
        async Task<bool> Try(FocusSession value,FocusPreferences prefs){try{await Store.StartAsync(value,prefs);return true;}catch(InvalidOperationException){return false;}}
        var results=await Task.WhenAll(Try(one,Minute),Try(two,new(2,2,2)));Assert.Single(results,result=>result);Assert.NotNull(await Store.ActiveAsync());
        Assert.Equal(1,(await Store.ReadAsync("device")).Count+(await Store.ReadAsync("other")).Count);
    }
    [Fact]
    public async Task StaleCheckpointAndHistoryRewrite_AreRejectedWithoutDoubleCounting()
    {
        var task=new FocusTask("device",Guid.NewGuid(),"Task",RepeatState.None);var first=await Store.StartAsync(FocusRules.Start("device",task,FocusKind.Focus,Minute,clock.Now),Minute);
        var next=FocusRules.Advance(first.Session,TimeSpan.FromSeconds(5),clock.Now.AddSeconds(5));var saved=await Store.SaveAsync(first,next);
        await Assert.ThrowsAsync<FocusConflictException>(()=>Store.SaveAsync(first,next));await Assert.ThrowsAsync<ArgumentException>(()=>Store.SaveAsync(saved,next with{Title="Rewrite"}));
        var paused=await Store.SaveAsync(saved,FocusRules.Pause(next,clock.Now.AddSeconds(5)));await Assert.ThrowsAsync<ArgumentException>(()=>Store.SaveAsync(paused,paused.Session with{ElapsedTicks=TimeSpan.FromSeconds(6).Ticks}));
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks,FocusRules.TimeSpentTicks(await Store.ReadAsync("device")));
    }
    [Fact]
    public async Task StorageFailure_StopsAccrualAndRecoversWithoutInventingTime()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);Sql("CREATE TRIGGER FailFocus BEFORE UPDATE ON FocusSessions BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Tick(focus);clock.Advance(30);await focus.TickSafelyAsync();await Assert.ThrowsAsync<SqliteException>(()=>focus.ReadAsync("device"));Sql("DROP TRIGGER FailFocus;");
        var recovered=await focus.ReadAsync("device");Assert.Null(recovered.Active);Assert.Equal(0,FocusRules.TimeSpentTicks(recovered.History));Assert.Equal(FocusStatus.Interrupted,Assert.Single(recovered.History).Session.Status);
    }
    [Fact]
    public async Task FutureSchemaAndCorruptIdentity_FailWithoutReset()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await focus.StopAsync("device",(await focus.ReadAsync("device")).Active!.Session.Id);
        var entry=Assert.Single(await Store.ReadAsync("device"));
        using(var connection=new SqliteConnection($"Data Source={PathFor("focus.db3")};Pooling=False"))
        {connection.Open();using var command=connection.CreateCommand();command.CommandText="UPDATE FocusSessions SET State=$state;";command.Parameters.AddWithValue("$state",JsonSerializer.Serialize(entry.Session with{Scope="foreign"}));command.ExecuteNonQuery();}
        await Assert.ThrowsAsync<InvalidDataException>(()=>Store.ReadAsync("device"));Sql("PRAGMA user_version=2;");await Assert.ThrowsAsync<InvalidDataException>(()=>Store.ReadAsync("device"));
        using var check=new SqliteConnection($"Data Source={PathFor("focus.db3")};Pooling=False");check.Open();using var query=check.CreateCommand();query.CommandText="PRAGMA user_version";Assert.Equal(2L,query.ExecuteScalar());query.CommandText="SELECT count(*) FROM FocusSessions";Assert.Equal(1L,query.ExecuteScalar());
    }
    [Fact]
    public async Task ViewModel_PreservesDraftSettingsDuringPollAndDerivesHistoryTotals()
    {
        var id=await TaskAsync();var focus=Service();using var vm=new FocusViewModel(focus,Catalog,session);await vm.RefreshCommand.ExecuteAsync();vm.SelectedTask=Assert.Single(vm.Tasks);vm.FocusMinutes="invalid";await vm.PollAsync();Assert.Equal("invalid",vm.FocusMinutes);await vm.StartFocusCommand.ExecuteAsync();Assert.NotEmpty(vm.Error);Assert.Empty(await Store.ReadAsync("device"));
        vm.FocusMinutes="1";vm.ShortBreakMinutes="1";vm.LongBreakMinutes="1";await vm.StartFocusCommand.ExecuteAsync();await Tick(focus);await vm.PollAsync();Assert.Equal("00:00:55",vm.Timer);Assert.Equal("00:00:05",Assert.Single(vm.TaskTotals).TimeSpent);
        await vm.StopCommand.ExecuteAsync();await tasks.DeleteAsync(id);await vm.RefreshCommand.ExecuteAsync();Assert.Empty(vm.Tasks);Assert.Single(vm.TaskTotals);Assert.Single(vm.History);Assert.Contains("00:00:05",vm.Totals);
    }
    [Fact]
    public async Task FourFocusBlocks_SuggestManualLongBreakAndBreakCompletionResetsSuggestion()
    {
        var id=await TaskAsync();var focus=Service();using var vm=new FocusViewModel(focus,Catalog,session);
        for(var i=0;i<4;i++){await focus.StartAsync("device",FocusKind.Focus,id,Minute);await FinishMinute(focus);}
        await vm.RefreshCommand.ExecuteAsync();Assert.Contains("Consider a long break",vm.Suggestion);await focus.StartAsync("device",FocusKind.LongBreak,null,Minute);await FinishMinute(focus);await vm.PollAsync();Assert.StartsWith("0 completed",vm.Suggestion);
        Assert.Equal(TimeSpan.FromMinutes(4).Ticks,FocusRules.TimeSpentTicks((await focus.ReadAsync("device")).History));Assert.False((await tasks.GetByIdAsync(id))!.IsCompleted);
    }
    [Fact]
    public async Task FractionalSessions_SumTicksWithoutRoundingEachSession()
    {
        var id=await TaskAsync();var focus=Service();
        for(var i=0;i<4;i++)
        {await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus,0.25);var active=(await focus.ReadAsync("device")).Active!;await focus.StopAsync("device",active.Session.Id);}
        var snapshot=await focus.ReadAsync("device");Assert.Equal(4,snapshot.History.Count);Assert.Equal(TimeSpan.TicksPerSecond,FocusRules.TimeSpentTicks(snapshot.History));
    }
    private sealed class GatedSource(IFocusTaskSource inner) : IFocusTaskSource
    {
        public TaskCompletionSource Arrived { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanAccess(string scope)=>inner.CanAccess(scope);
        public async Task<IReadOnlyList<FocusTask>> ReadAsync(string scope)
        {var snapshot=await inner.ReadAsync(scope);Arrived.TrySetResult();await Release.Task;return snapshot;}
    }
    [Fact]
    public async Task AccountChangeDuringTaskLookup_CannotCreateAnOldAccountSession()
    {
        var scope=await LoginAsync();var id=Guid.NewGuid();await Sync.QueueAsync(scope,id,new("Private"));var source=new GatedSource(Catalog);var focus=Service(source);
        var starting=focus.StartAsync(scope,FocusKind.Focus,id,Minute);await source.Arrived.Task;await session.SignOutAsync();source.Release.TrySetResult();
        await Assert.ThrowsAsync<ApiClientException>(()=>starting);Assert.Empty(await Store.ReadAsync(scope));Assert.Null((await focus.ReadAsync("device")).Active);
    }
    [Fact]
    public async Task CompletedMissingAndForeignTasks_CannotStartFocusOrSaveSettings()
    {
        var id=await TaskAsync();await tasks.SetCompletedAsync(id,true);var focus=Service();
        await Assert.ThrowsAsync<ArgumentException>(()=>focus.StartAsync("device",FocusKind.Focus,id,Minute));
        await Assert.ThrowsAsync<ArgumentException>(()=>focus.StartAsync("device",FocusKind.Focus,Guid.NewGuid(),Minute));
        Assert.Empty(await Store.ReadAsync("device"));Assert.Equal(new FocusPreferences(),await Store.PreferencesAsync("device"));
        var alice=await LoginAsync();var secret=Guid.NewGuid();await Sync.QueueAsync(alice,secret,new("Secret"));await session.SignOutAsync();var bob=await LoginAsync("bob@example.invalid");
        await Assert.ThrowsAsync<ArgumentException>(()=>focus.StartAsync(bob,FocusKind.Focus,secret,Minute));Assert.Empty(await Store.ReadAsync(bob));
    }
    [Fact]
    public async Task SameAccountAndTaskIdsOnAnotherOrigin_HaveSeparateHistory()
    {
        var scope=await LoginAsync();var user=session.Current!.User;var id=Guid.NewGuid();await Sync.QueueAsync(scope,id,new("First origin"));var focus=Service();await focus.StartAsync(scope,FocusKind.Focus,id,Minute);await Tick(focus);await focus.StopAsync(scope,(await focus.ReadAsync(scope)).Active!.Session.Id);
        await session.SignOutAsync();settings.ApiBaseUrl="https://other.invalid/";await session.SaveAsync(new("another-token","Bearer",clock.Now.AddMinutes(15),user),session.Endpoint,session.Version);
        var other=SqliteSyncStore.Partition(session.Current!.Origin,user.Id);await Sync.QueueAsync(other,id,new("Other origin"));Assert.Empty((await focus.ReadAsync(other)).History);await Assert.ThrowsAsync<ApiClientException>(()=>focus.ReadAsync(scope));
        await focus.StartAsync(other,FocusKind.Focus,id,Minute);await Tick(focus);await focus.StopAsync(other,(await focus.ReadAsync(other)).Active!.Session.Id);
        Assert.Equal("First origin",Assert.Single(await Store.ReadAsync(scope)).Session.Title);Assert.Equal("Other origin",Assert.Single(await Store.ReadAsync(other)).Session.Title);
    }
    [Fact]
    public async Task SuspendedApp_RejectsNewSessionsUntilResumeWithoutAutoRestarting()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);await Tick(focus);await focus.SuspendAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>focus.StartAsync("device",FocusKind.Focus,id,Minute));await focus.ResumeAppSafelyAsync();Assert.Null((await focus.ReadAsync("device")).Active);
        await focus.StartAsync("device",FocusKind.ShortBreak,null,Minute);Assert.Equal(FocusKind.ShortBreak,(await focus.ReadAsync("device")).Active!.Session.Kind);
    }
    [Fact]
    public async Task OldWindowStop_CannotStopANewerSession()
    {
        var id=await TaskAsync();var focus=Service();await focus.StartAsync("device",FocusKind.Focus,id,Minute);var old=(await focus.ReadAsync("device")).Active!;await focus.StopAsync("device",old.Session.Id);await focus.StartAsync("device",FocusKind.ShortBreak,null,Minute);
        await Assert.ThrowsAsync<FocusConflictException>(()=>focus.StopAsync("device",old.Session.Id));Assert.Equal(FocusKind.ShortBreak,(await focus.ReadAsync("device")).Active!.Session.Kind);
    }
}
