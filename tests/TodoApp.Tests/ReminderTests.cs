using Microsoft.Data.Sqlite;
using TodoApp.Domain.Reminders;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

internal sealed class FakeReminderPlatform : IReminderPlatform
{
    public bool SupportsScheduling { get; set; } = true;
    public string DeliveryDescription => SupportsScheduling ? "Test OS scheduling" : "Test running-app scheduling";
    public ReminderPermission Permission { get; set; } = ReminderPermission.Allowed;
    public bool FailSchedule { get; set; }
    public int? FailCancelId { get; set; }
    public bool FailShow { get; set; }
    public int PermissionRequests { get; private set; }
    public Dictionary<int, DateTimeOffset> Scheduled { get; } = new();
    public List<int> Shown { get; } = new();
    public List<int> Canceled { get; } = new();
    public TaskCompletionSource? ScheduleArrived { get; set; }
    public TaskCompletionSource? ScheduleGate { get; set; }
    public Task<ReminderPermission> PermissionAsync(bool request)
    { if (request) PermissionRequests++; return Task.FromResult(Permission); }
    public async Task ScheduleAsync(int id, DateTimeOffset when)
    {
        ScheduleArrived?.TrySetResult();
        if (ScheduleGate is not null) await ScheduleGate.Task;
        if (FailSchedule) throw new IOException("Test native scheduling error");
        Scheduled[id] = when;
    }
    public Task ShowAsync(int id)
    { Shown.Add(id); if (FailShow) throw new IOException("Uncertain native show"); return Task.CompletedTask; }
    public Task CancelAsync(int id)
    { Canceled.Add(id); if (FailCancelId == id) throw new IOException("Test native cancellation error"); Scheduled.Remove(id); return Task.CompletedTask; }
}

public sealed class ReminderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_reminders_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly FakeReminderPlatform platform = new();
    private SqliteLocalTaskService Tasks => new(Path.Combine(directory, "tasks.db3"));
    private SqliteSyncStore Sync => new(Path.Combine(directory, "sync.db3"));
    private SqliteReminderStore Store => new(Path.Combine(directory, "reminders.db3"));
    private ReminderService Service => new(Store, new(Tasks, Sync, client.Session), platform, client.Session, client.Clock);
    public void Dispose() { client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private async Task<ReminderTask> TaskAsync(string title = "Learn reminders", DateOnly? due = null)
    {
        var task = await Tasks.CreateAsync(new TaskDetails(title, DueDate: due));
        return new("device", task.Id, task.Title, task.DueDate, task.IsCompleted);
    }
    private Task SaveAsync(ReminderService service, ReminderTask task) => service.SaveAsync(task,
        client.Clock.Now.UtcDateTime.Date, TimeSpan.FromHours(1), TimeZoneInfo.Utc);

    [Fact]
    public async Task SavingReminder_ReopensWithStableNativeIdAndExplicitUtcInstant()
    {
        var task = await TaskAsync(); var service = Service;
        await SaveAsync(service, task);
        var first = Assert.Single(await Store.ReadAsync());
        Assert.True(first.Enabled); Assert.Equal(client.Clock.Now.AddHours(1), first.FireAtUtc);
        Assert.Equal(first.FireAtUtc, platform.Scheduled[first.Id]);
        await service.SaveAsync(task, client.Clock.Now.UtcDateTime.Date, TimeSpan.FromHours(2), TimeZoneInfo.Utc);
        var replaced = Assert.Single(await Store.ReadAsync());
        Assert.Equal(first.Id, replaced.Id); Assert.Equal(client.Clock.Now.AddHours(2), replaced.FireAtUtc);
        Assert.Equal(replaced.FireAtUtc, platform.Scheduled[first.Id]);
    }
    [Fact]
    public async Task DueDateAlone_DoesNotCreateOrScheduleAnAlert()
    {
        await TaskAsync(due: new DateOnly(2026, 11, 13));
        await Service.RefreshSafelyAsync();
        Assert.Empty(await Store.ReadAsync()); Assert.Empty(platform.Scheduled); Assert.Equal(0, platform.PermissionRequests);
    }
    [Fact]
    public async Task PermissionDenial_RetainsChoiceWithoutPromptingAndCanBeRetried()
    {
        platform.Permission = ReminderPermission.Denied;
        var task = await TaskAsync(); var service = Service;
        await SaveAsync(service, task);
        Assert.Empty(platform.Scheduled); Assert.True(Assert.Single(await Store.ReadAsync()).Enabled);
        Assert.Equal(0, platform.PermissionRequests);
        await service.RequestPermissionAsync(); Assert.Equal(1, platform.PermissionRequests); Assert.Contains("blocked", service.Status);
        platform.Permission = ReminderPermission.Allowed;
        await service.RefreshSafelyAsync(); Assert.Single(platform.Scheduled);
    }
    [Theory]
    [InlineData("complete")]
    [InlineData("delete")]
    [InlineData("due")]
    public async Task TaskMutations_CancelWithoutAutomaticallyRearming(string mutation)
    {
        var task = await TaskAsync(due: new DateOnly(2026, 11, 15)); var service = Service;
        await SaveAsync(service, task); var row = Assert.Single(await Store.ReadAsync());
        var decorated = new ReminderAwareTaskService(Tasks, service);
        if (mutation == "complete") await decorated.SetCompletedAsync(task.TaskId, true);
        if (mutation == "delete") await decorated.DeleteAsync(task.TaskId);
        if (mutation == "due") await decorated.UpdateAsync(task.TaskId, new TaskDetails(task.Title, DueDate: task.DueDate!.Value.AddDays(1)));
        Assert.False(Assert.Single(await Store.ReadAsync()).Enabled); Assert.DoesNotContain(row.Id, platform.Scheduled.Keys);
        if (mutation == "complete") await decorated.SetCompletedAsync(task.TaskId, false);
        await service.RefreshSafelyAsync(); Assert.Empty(platform.Scheduled);
    }
    [Fact]
    public async Task Cancel_IsDurableAndNativeRemovalFailuresRemainRetryable()
    {
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        var row = Assert.Single(await Store.ReadAsync()); platform.FailCancelId = row.Id;
        await service.CancelAsync(row.Key);
        Assert.False(Assert.Single(await Store.ReadAsync()).Enabled); Assert.Contains("failed", service.Status);
        platform.FailCancelId = null; await Service.RefreshSafelyAsync(); Assert.Empty(platform.Scheduled);
    }
    [Fact]
    public async Task NativeFailureAndThrowingUiObservers_DoNotTurnACommittedTaskCreateIntoAFailure()
    {
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        platform.FailSchedule = true; service.Changed += () => throw new InvalidOperationException("Test UI observer failure");
        var created = await new ReminderAwareTaskService(Tasks, service).CreateAsync(new TaskDetails("Still saved once"));
        Assert.NotNull(await Tasks.GetByIdAsync(created.Id)); Assert.Equal(2, (await Tasks.GetTasksAsync()).Count);
        Assert.Contains("failed", service.Status);
    }
    [Fact]
    public async Task TitleEditsKeepTheInstantWhileTaskContentStaysOutOfNativePayload()
    {
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        var original = Assert.Single(await Store.ReadAsync());
        await new ReminderAwareTaskService(Tasks, service).UpdateAsync(task.TaskId, new TaskDetails("Renamed task"));
        var changed = Assert.Single(await Store.ReadAsync());
        Assert.True(changed.Enabled); Assert.Equal(original.FireAtUtc, changed.FireAtUtc); Assert.Equal("Renamed task", changed.Title);
        // Native contract accepts only numeric ID and time, never a title/account/token.
        Assert.Equal(original.FireAtUtc, platform.Scheduled[original.Id]);
    }
    [Fact]
    public async Task RunningAppDelivery_IsAttemptedOnceAndNotRepeatedAfterClockRollback()
    {
        platform.SupportsScheduling = false;
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        Assert.Empty(platform.Shown); Assert.Empty(platform.Scheduled);
        client.Clock.Now = client.Clock.Now.AddHours(1).AddSeconds(10);
        await service.RefreshSafelyAsync(); Assert.Single(platform.Shown);
        client.Clock.Now = client.Clock.Now.AddHours(-2); await service.RefreshSafelyAsync();
        client.Clock.Now = client.Clock.Now.AddHours(2); await Service.RefreshSafelyAsync();
        Assert.Single(platform.Shown); Assert.True(Assert.Single(await Store.ReadAsync()).Attempted);
    }
    [Fact]
    public async Task MissedRunningAppReminder_DoesNotFloodOnNextLaunch()
    {
        platform.SupportsScheduling = false;
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        client.Clock.Now = client.Clock.Now.AddDays(1);
        await Service.RefreshSafelyAsync(); Assert.Empty(platform.Shown);
        Assert.Contains("Missed", Assert.Single(await Store.ReadAsync()).Status);
    }
    [Fact]
    public async Task UncertainNativeShow_IsNotAutomaticallyRetried()
    {
        platform.SupportsScheduling = false;
        var task = await TaskAsync(); var service = Service; await SaveAsync(service, task);
        client.Clock.Now = client.Clock.Now.AddHours(1).AddSeconds(5); platform.FailShow = true;
        await service.RefreshSafelyAsync(); await Service.RefreshSafelyAsync();
        Assert.Single(platform.Shown); Assert.True(Assert.Single(await Store.ReadAsync()).Attempted);
    }
    [Fact]
    public async Task SchedulerQueuesOnlyNearest32AndRefillsAfterCancellation()
    {
        var service = Service;
        for (var i = 0; i < 34; i++)
        {
            var task = await TaskAsync($"Task {i}");
            await Store.PutAsync(new ReminderRecord { Scope = task.Scope, TaskId = task.TaskId, Title = task.Title,
                Enabled = true, FireAtUtc = client.Clock.Now.AddMinutes(5 + i), WallTime = client.Clock.Now.AddMinutes(5 + i).UtcDateTime, TimeZoneId = "UTC" });
        }
        await service.RefreshSafelyAsync(); Assert.Equal(32, platform.Scheduled.Count);
        var first = (await Store.ReadAsync())[0]; await service.CancelAsync(first.Key);
        Assert.Equal(32, platform.Scheduled.Count); Assert.DoesNotContain(first.Id, platform.Scheduled.Keys);
    }
    [Fact]
    public async Task SignOutAndAccountSwitch_SuspendAccountAlertsWithoutAffectingDeviceAlerts()
    {
        await client.LoginAsync(); var firstAccount = client.Account;
        var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, firstAccount.User.Id); var id = Guid.NewGuid();
        await Sync.QueueAsync(scope, id, new("Account A"));
        var task = new ReminderTask(scope, id, "Account A", null, false); var service = Service;
        await SaveAsync(service, task); await SaveAsync(service, await TaskAsync()); Assert.Equal(2, platform.Scheduled.Count);
        await client.Session.SignOutAsync(); await service.RefreshSafelyAsync(); Assert.Single(platform.Scheduled);
        Assert.Single(await service.ReadAsync());
        client.Account = client.Auth("other-test-token", "other@example.invalid"); await client.LoginAsync();
        await service.RefreshSafelyAsync(); Assert.Single(platform.Scheduled); Assert.Single(await service.ReadAsync());
        client.Account = firstAccount; await client.LoginAsync(); await service.RefreshSafelyAsync(); Assert.Equal(2, platform.Scheduled.Count);
    }
    [Fact]
    public async Task SessionChangeDuringNativeScheduling_CancelsTheStaleSchedule()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        var id = Guid.NewGuid(); await Sync.QueueAsync(scope, id, new("Private")); var service = Service;
        platform.ScheduleArrived = new(TaskCreationOptions.RunContinuationsAsynchronously); platform.ScheduleGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = SaveAsync(service, new ReminderTask(scope, id, "Private", null, false));
        await platform.ScheduleArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.Session.SignOutAsync(); platform.ScheduleGate.SetResult(); await running;
        Assert.Empty(platform.Scheduled); await service.RefreshSafelyAsync(); Assert.Empty(platform.Scheduled);
    }
    [Fact]
    public async Task SyncWriteHook_CancelsACompletedCachedTaskAndDoesNotFailCommittedWrites()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        var sync = Sync; var id = Guid.NewGuid(); await sync.QueueAsync(scope, id, new("Synced"));
        var service = Service; sync.Written += service.RefreshSafelyAsync;
        await SaveAsync(service, new ReminderTask(scope, id, "Synced", null, false));
        await sync.QueueAsync(scope, id, new("Synced", IsCompleted: true));
        Assert.Empty(platform.Scheduled); Assert.False(Assert.Single(await Store.ReadAsync()).Enabled);
        sync.Written += () => throw new IOException("Test observer");
        await sync.QueueAsync(scope, id, new("Still queued"));
        Assert.Equal("Still queued", (await sync.ReadAsync(scope)).Rows[id].Local!.Title);
    }
    [Fact]
    public async Task OneCancellationFailure_DoesNotSkipOtherCancellations()
    {
        var service = Service; var a = await TaskAsync("A"); var b = await TaskAsync("B");
        await SaveAsync(service, a); await SaveAsync(service, b);
        var rows = await Store.ReadAsync(); platform.FailCancelId = rows[0].Id;
        await Tasks.SetCompletedAsync(a.TaskId, true); await Tasks.SetCompletedAsync(b.TaskId, true);
        await service.RefreshSafelyAsync();
        Assert.Contains(rows[1].Id, platform.Canceled); Assert.DoesNotContain(rows[1].Id, platform.Scheduled.Keys);
        Assert.All(await Store.ReadAsync(), r => Assert.False(r.Enabled));
    }
    [Fact]
    public async Task ViewModel_InvalidTimeKeepsDraftAndPermissionsAreExplicit()
    {
        var task = await TaskAsync(); var service = Service;
        using var vm = new RemindersViewModel(service, client.Session);
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks);
        vm.Date = client.Clock.Now.UtcDateTime.Date.AddYears(-1); vm.Time = TimeSpan.FromHours(1);
        await vm.SaveCommand.ExecuteAsync(); Assert.NotEmpty(vm.Error); Assert.Equal(task.TaskId, vm.SelectedTask!.TaskId);
        Assert.Empty(await Store.ReadAsync()); Assert.Equal(0, platform.PermissionRequests);
        await vm.PermissionCommand.ExecuteAsync(); Assert.Equal(1, platform.PermissionRequests);
    }
    [Fact]
    public async Task CorruptOrFutureSchema_IsRejectedWithoutResettingSavedData()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reminders.db3");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync());
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version;";
            Assert.Equal(99L, command.ExecuteScalar());
        }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=1; CREATE TABLE ReminderSettings (Id INTEGER PRIMARY KEY, State TEXT NOT NULL); INSERT INTO ReminderSettings VALUES (1,'{broken');";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => Store.ReadAsync());
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT State FROM ReminderSettings WHERE Id=1;";
            Assert.Equal("{broken", command.ExecuteScalar());
        }

    }
    [Fact]
    public async Task NativeIds_AreUniqueAcrossAccountAndDeviceTaskKeys()
    {
        var id = Guid.NewGuid();
        await Store.PutAsync(new ReminderRecord { Scope = "device", TaskId = id });
        await Store.PutAsync(new ReminderRecord { Scope = "https://api.invalid/|account", TaskId = id });
        var rows = await Store.ReadAsync(); Assert.Equal(2, rows.Select(r => r.Id).Distinct().Count());
        var original = rows[0].Id; rows[0].Enabled = false; await Store.PutAsync(rows[0]);
        Assert.Contains(await Store.ReadAsync(), r => r.Id == original && !r.Enabled);
    }
}

public sealed class ReminderTimeTests
{
    private static TimeZoneInfo DaylightZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026,1,1), new DateTime(2026,12,31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test eastern", TimeSpan.FromHours(-5), "Test eastern", "Standard", "Daylight", [rule]);
    }
    [Theory]
    [InlineData(3, 8, 2, 30)]
    [InlineData(11, 1, 1, 30)]
    public void SkippedAndRepeatedDaylightTimes_AreRejected(int month, int day, int hour, int minute) =>
        Assert.Throws<ArgumentException>(() => ReminderTime.Resolve(new DateTime(2026,month,day), new TimeSpan(hour,minute,0), DaylightZone(), new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero)));
    [Fact]
    public void ExplicitZone_ResolvesToAFixedUtcInstant()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +8", TimeSpan.FromHours(8), "Test +8", "Test +8");
        var resolved = ReminderTime.Resolve(new DateTime(2026,11,14), new TimeSpan(9,30,0), zone, new DateTimeOffset(2026,11,13,0,0,0,TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026,11,14,1,30,0,TimeSpan.Zero), resolved);
        Assert.Equal(TimeSpan.Zero, resolved.Offset);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void InvalidTimeOfDay_IsRejected(int hours) => Assert.Throws<ArgumentException>(() => ReminderTime.Resolve(new DateTime(2026,11,14), TimeSpan.FromHours(hours), TimeZoneInfo.Utc, new DateTimeOffset(2026,11,13,0,0,0,TimeSpan.Zero)));
    [Fact]
    public void PastAndTooDistantTimes_AreRejected()
    {
        var now = new DateTimeOffset(2026,11,13,0,0,0,TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => ReminderTime.Resolve(now.Date, TimeSpan.Zero, TimeZoneInfo.Utc, now));
        Assert.Throws<ArgumentException>(() => ReminderTime.Resolve(now.AddDays(366).Date, TimeSpan.FromHours(1), TimeZoneInfo.Utc, now));
    }
}
