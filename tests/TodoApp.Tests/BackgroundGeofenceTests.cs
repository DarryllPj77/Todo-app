using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Location;
using TodoApp.Domain.Recurrence;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class BackgroundGeofenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_background_geo_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly Keys keys = new();
    private readonly InMemoryLocalTaskService tasks = new();
    private readonly Position position = new();
    private readonly Notifications notifications = new();
    private readonly Platform platform = new();
    private readonly SqliteLocationReminderStore places;
    private readonly SqliteBackgroundGeofenceStore journal;
    private readonly Blocker blocker;
    private readonly GeofenceProcessLease lease;
    private readonly LocationTaskCatalog source;
    private LocationReminderService Reminders(IReminderPlatform? delivery = null) => new(places, source, position, delivery ?? notifications, client.Session, client.Clock);
    private BackgroundGeofenceService Service => new(journal, Reminders(), platform, blocker, lease, client.Clock);
    private BackgroundGeofenceService Worker => new(journal, Reminders(new BackgroundNotificationGuard(notifications, Allowed)), platform, blocker, lease, client.Clock);
    public BackgroundGeofenceTests()
    {
        places = new(Path.Combine(directory, "location.db3"), keys); journal = new(Path.Combine(directory, "background.db3"), keys);
        blocker = new(Path.Combine(directory, "control.db3")); lease = new(Path.Combine(directory, "native.lock"));
        source = new(tasks, new(Path.Combine(directory, "sync.db3")), client.Session, client.Clock); Outside();
    }
    public void Dispose() { client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private async Task<bool> Allowed()
    { var run = await journal.ReadAsync(); return !blocker.IsBlocked && run is { State: BackgroundGeofenceState.Active } && client.Clock.Now >= run.StartedUtc && client.Clock.Now < run.ExpiresUtc; }
    private void Outside() => position.Fix = new(0, .02, 10, client.Clock.Now);
    private void Inside() => position.Fix = new(0, 0, 10, client.Clock.Now);
    private async Task<LocationReminder> Arm()
    { var row = await tasks.CreateAsync(new("Private background task")); return await Reminders().ArmAsync("device", null, new(row.Id, "Private center", 0, 0, 200), default); }
    private Task Process(BackgroundGeofenceRun run, NativeGeofenceTransition transition, PositionFix? fix = null, string? nativeId = null) =>
        Worker.ProcessAsync(() => [new(nativeId ?? (transition == NativeGeofenceTransition.Exited ? run.ExitId : run.EntryId), transition, fix ?? position.Fix)], default);
    private void Sql(string file, string sql)
    { using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, file)};Pooling=False"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private sealed class Keys : ILocationEncryptionKeyStore
    {
        public byte[]? Value = RandomNumberGenerator.GetBytes(32);
        public Task<byte[]> GetAsync(bool allowCreate)
        { if (Value is null) throw new InvalidDataException("Protected key missing"); return Task.FromResult(Value.ToArray()); }
    }
    private sealed class Blocker(string path) : IBackgroundGeofenceBlocker
    {
        public readonly SqliteBackgroundGeofenceControl Control = new(path);
        public Action? Enabling;
        public bool IsBlocked => Control.Read().Blocked;
        public long Generation => Control.Read().Generation;
        public long Block() => Control.Block();
        public void Enable(long generation) { Enabling?.Invoke(); Control.Enable(generation); }
    }
    private sealed class Position : IForegroundLocationProvider
    {
        public PositionFix Fix = null!; public int Reads;
        public Task<PositionFix> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult(Fix); }
    }
    private sealed class Notifications : IReminderPlatform
    {
        public bool SupportsScheduling => false; public string DeliveryDescription => "Test";
        public ReminderPermission Permission = ReminderPermission.Allowed;
        public List<int> Shown = new(), Canceled = new(); public List<bool> Requests = new();
        public bool Fail; public Action? Showing;
        public Task<ReminderPermission> PermissionAsync(bool request) { Requests.Add(request); return Task.FromResult(Permission); }
        public Task ScheduleAsync(int id, DateTimeOffset when) => throw new NotSupportedException();
        public Task ShowAsync(int id) { Shown.Add(id); Showing?.Invoke(); if (Fail) throw new IOException("Uncertain delivery"); return Task.CompletedTask; }
        public Task CancelAsync(int id) { Canceled.Add(id); return Task.CompletedTask; }
    }
    private sealed class Platform : IBackgroundGeofencePlatform
    {
        public bool Supported = true, FailAccess, FailRegister, FailCleanup;
        public bool IsSupported => Supported; public string Description => "Test background capability";
        public int Accesses, Registrations, Cleanups; public BackgroundGeofenceRun? Registered;
        public Func<Task>? Registering;
        public Task RequestAccessAsync(CancellationToken token) { Accesses++; if (FailAccess) throw new InvalidOperationException("Denied"); return Task.CompletedTask; }
        public async Task RegisterAsync(BackgroundGeofenceRun run, CancellationToken token)
        { Registrations++; Registered = run; if (Registering is not null) await Registering(); if (FailRegister) throw new IOException("Partial registration"); }
        public Task CleanupAsync() { Cleanups++; if (FailCleanup) throw new IOException("Cleanup failed"); Registered = null; return Task.CompletedTask; }
    }
    [Fact]
    public async Task UnsupportedBuild_DoesNotTouchKeysPermissionsOrLocation()
    {
        var row = await Arm(); platform.Supported = false; var before = position.Reads;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.StartAsync(row, default));
        Assert.Equal(0, platform.Accesses); Assert.Equal(before, position.Reads); Assert.Null(await journal.ReadAsync());
    }
    [Fact]
    public async Task AccountScope_IsNeverAuthorizedForHeadlessRead()
    { var row = await Arm(); await Assert.ThrowsAsync<ArgumentException>(() => Service.StartAsync(row with { Scope = "account" }, default)); Assert.Equal(0, platform.Accesses); }
    [Fact]
    public async Task RestartedWorker_UsesEncryptedRunAndOutsideEvidence_ThenConsumesOneAttempt()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!;
        Assert.Equal(BackgroundGeofenceState.Active, run.State); Assert.True(run.OutsideObserved); Assert.False(blocker.IsBlocked);
        Inside(); await Process(run, NativeGeofenceTransition.Entered); await Process(run, NativeGeofenceTransition.Entered);
        Assert.Single(notifications.Shown); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered);
        Assert.Equal(BackgroundGeofenceState.Stopped, (await journal.ReadAsync())!.State);
        Assert.Equal(ArrivalState.Attempted, Assert.Single(await places.ReadAsync("device")).State);
        Assert.Equal(1, position.Reads); Assert.All(notifications.Requests, request => Assert.False(request));
    }
    [Fact]
    public async Task InitialInside_DoesNotReuseOutsideFromOldManualCheck()
    {
        var row = await Arm(); await Reminders().CheckAsync("device", default); row = Assert.Single(await places.ReadAsync("device")); Inside();
        await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!; Assert.False(run.OutsideObserved);
        await Process(run, NativeGeofenceTransition.Entered); Assert.Empty(notifications.Shown);
        Outside(); await Process(run, NativeGeofenceTransition.Exited); Inside(); await Process(run, NativeGeofenceTransition.Entered); Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task StopBeforeEnable_CannotBeOverwrittenByPendingStart()
    {
        var row = await Arm(); blocker.Enabling = () => blocker.Block();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.StartAsync(row, default));
        Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(BackgroundGeofenceState.Stopped, (await journal.ReadAsync())!.State);
    }
    [Fact]
    public void SeparateControlInstances_EnforceAtomicStopGeneration()
    {
        var other = new SqliteBackgroundGeofenceControl(Path.Combine(directory, "control.db3"));
        var generation = blocker.Block(); other.Block(); Assert.Throws<OperationCanceledException>(() => blocker.Enable(generation)); Assert.True(other.Read().Blocked);
    }
    [Fact]
    public async Task CancelledStart_CleansPartialRegistrationAndRemainsBlocked()
    {
        var row = await Arm(); using var cancellation = new CancellationTokenSource(); platform.Registering = () => { cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.StartAsync(row, cancellation.Token));
        Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(0, position.Reads);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task PermissionOrRegistrationFailure_NeverEnablesRun(bool access)
    {
        var row = await Arm(); platform.FailAccess = access; platform.FailRegister = !access;
        await Assert.ThrowsAnyAsync<Exception>(() => Service.StartAsync(row, default)); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(0, position.Reads);
    }
    [Theory]
    [InlineData(ReminderPermission.Denied)] [InlineData(ReminderPermission.Unavailable)]
    public async Task BlockedNotificationPermission_DoesNotRegisterOrReadLocation(ReminderPermission permission)
    {
        var row = await Arm(); notifications.Permission = permission;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.StartAsync(row, default)); Assert.Equal(0, platform.Registrations); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task StaleSelection_CannotRegisterNewerArm()
    {
        var row = await Arm(); await Reminders().ArmAsync("device", row, new(row.TaskId, "New center", 0, 0, 200), default);
        await Assert.ThrowsAsync<LocationReminderConflictException>(() => Service.StartAsync(row, default)); Assert.Equal(0, platform.Registrations);
    }
    [Theory]
    [InlineData("complete")] [InlineData("delete")] [InlineData("repeat")]
    public async Task ChangedSourceTask_StopsBeforeReportDelivery(string change)
    {
        var row = await Arm(); await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!;
        if (change == "complete") await tasks.SetCompletedAsync(row.TaskId, true);
        else if (change == "delete") await tasks.DeleteAsync(row.TaskId);
        else await tasks.SetRecurrenceRuleAsync(row.TaskId, new(RepeatFrequency.Daily, 1, new(2026, 11, 14)));
        Inside(); await Assert.ThrowsAsync<LocationReminderConflictException>(() => Process(run, NativeGeofenceTransition.Entered));
        Assert.Empty(notifications.Shown); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered);
    }
    [Fact]
    public async Task ReplacementArmAndOldNativeIdentity_CannotNotify()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var old = (await journal.ReadAsync())!;
        row = Assert.Single(await places.ReadAsync("device")); await Reminders().ArmAsync("device", row, new(row.TaskId, "New", 0, 0, 200), default);
        Inside(); await Assert.ThrowsAsync<LocationReminderConflictException>(() => Process(old, NativeGeofenceTransition.Entered)); Assert.Empty(notifications.Shown);
    }
    [Fact]
    public async Task StaleNativeIdFromPreviousRun_IsIgnored()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var old = (await journal.ReadAsync())!; await Service.StopAsync();
        row = Assert.Single(await places.ReadAsync("device")); await Service.StartAsync(row, default); var current = (await journal.ReadAsync())!;
        Inside(); await Process(current, NativeGeofenceTransition.Entered, nativeId: old.EntryId);
        Assert.Empty(notifications.Shown); Assert.False(blocker.IsBlocked); Assert.Equal(current.Id, (await journal.ReadAsync())!.Id);
    }
    [Theory]
    [InlineData("mock")] [InlineData("old")] [InlineData("future")] [InlineData("accuracy")] [InlineData("boundary")]
    public async Task UnreliableNativeReport_DoesNotConsume(string kind)
    {
        var row = await Arm(); await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!; Inside();
        var fix = kind switch
        {
            "mock" => position.Fix with { Mocked = true }, "old" => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(-3) },
            "future" => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(1) }, "accuracy" => position.Fix with { AccuracyMeters = 101 },
            _ => position.Fix with { Longitude = .0018 }
        };
        await Process(run, NativeGeofenceTransition.Entered, fix); Assert.Empty(notifications.Shown); Assert.False(blocker.IsBlocked);
    }
    [Fact]
    public async Task ExpiryStopsRunWithoutSamplingOrAutomaticRestoration()
    {
        var row = await Arm(); await Service.StartAsync(row, default); client.Clock.Now = client.Clock.Now.AddMinutes(30); await Service.ReconcileAsync(default);
        Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(1, platform.Registrations); Assert.Equal(1, position.Reads);
    }
    [Fact]
    public async Task NativeRemovalStopsRunWithoutReregistering()
    {
        var row = await Arm(); await Service.StartAsync(row, default); await Process((await journal.ReadAsync())!, NativeGeofenceTransition.Removed);
        Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(1, platform.Registrations);
    }
    [Fact]
    public async Task CleanupFailure_RetainsBlockedStoppedJournalForRetry()
    {
        var row = await Arm(); await Service.StartAsync(row, default); platform.FailCleanup = true;
        await Assert.ThrowsAsync<IOException>(() => Service.StopAsync()); Assert.True(blocker.IsBlocked); Assert.Equal(BackgroundGeofenceState.Stopped, (await journal.ReadAsync())!.State);
        platform.FailCleanup = false; await Service.StopAsync(); Assert.Null(platform.Registered);
    }
    [Fact]
    public async Task LostKey_StopStillBlocksAndAttemptsNativeCleanup()
    {
        var row = await Arm(); await Service.StartAsync(row, default); keys.Value = null;
        await Assert.ThrowsAsync<InvalidDataException>(() => Service.StopAsync()); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Null(keys.Value);
    }
    [Fact]
    public async Task HeadlessGuard_BlocksBeforeShowAndCancelsIfStopRacesDelivery()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var guarded = new BackgroundNotificationGuard(notifications, Allowed);
        notifications.Showing = () => blocker.Block();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.ShowAsync(row.NotificationId)); Assert.Single(notifications.Shown); Assert.Contains(row.NotificationId, notifications.Canceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.ShowAsync(row.NotificationId)); Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task NativeShowFailure_IsConsumedAcrossWorkerRestart()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!; Inside(); notifications.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => Process(run, NativeGeofenceTransition.Entered)); notifications.Fail = false;
        await Process(run, NativeGeofenceTransition.Entered); Assert.Single(notifications.Shown); Assert.Equal(ArrivalState.Attempted, Assert.Single(await places.ReadAsync("device")).State);
    }
    [Fact]
    public async Task BusyProcessLease_CannotDrainReports()
    {
        var row = await Arm(); await Service.StartAsync(row, default); using var held = await lease.AcquireAsync(default);
        using var cancellation = new CancellationTokenSource(50); var drained = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Worker.ProcessAsync(() => { drained = true; return []; }, cancellation.Token)); Assert.False(drained);
    }
    [Fact]
    public async Task DeviceRunSurvivesAccountChangeWithoutReadingAccountTokens()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!;
        await client.LoginAsync(); await client.Session.SignOutAsync(); Inside(); await Process(run, NativeGeofenceTransition.Entered);
        Assert.Single(notifications.Shown); Assert.Equal(0, client.Storage.ReadCount);
    }
    [Fact]
    public async Task Journal_IsEncryptedAndRejectsTamperingOrStaleRevision()
    {
        var row = await Arm(); await Service.StartAsync(row, default); var saved = (await journal.ReadAsync())!;
        var bytes = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "background.db3")));
        Assert.DoesNotContain("Private background task", bytes); Assert.DoesNotContain("Private center", bytes); Assert.DoesNotContain("Latitude", bytes);
        await Assert.ThrowsAsync<LocationReminderConflictException>(() => journal.SaveAsync(saved with { Revision = saved.Revision - 1 }, saved with { Revision = saved.Revision + 1 }));
        Sql("background.db3", "UPDATE BackgroundRun SET Revision=Revision+1;");
        await Assert.ThrowsAsync<InvalidDataException>(() => journal.ReadAsync()); await Assert.ThrowsAsync<InvalidDataException>(() => Service.StopAsync()); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered);
    }
    [Fact]
    public async Task UnreadableOrFutureControlData_FailsClosedWithoutReset()
    {
        blocker.Block(); Sql("control.db3", "PRAGMA user_version=99;");
        Assert.Throws<InvalidDataException>(() => blocker.Control.Read()); Assert.Throws<InvalidDataException>(() => blocker.Block());
        var row = await Arm(); await Assert.ThrowsAsync<InvalidDataException>(() => Service.StartAsync(row, default)); Assert.Equal(0, platform.Registrations);
    }
    [Fact]
    public async Task ViewModel_CanceledOrChangedConfirmationCannotOptIn()
    {
        var row = await Arm(); var navigation = new ClientNavigation { Confirm = false };
        using var vm = new LocationRemindersViewModel(Reminders(), client.Session, navigation, background: Service);
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedReminder = vm.Reminders.Single();
        await vm.StartBackgroundCommand.ExecuteAsync(); Assert.Equal(0, platform.Registrations);
        var answer = new TaskCompletionSource<bool>(); navigation.LocationConfirmation = () => answer.Task;
        var action = vm.StartBackgroundCommand.ExecuteAsync(); vm.Workspace = HierarchyWorkspace.SyncedAccount; answer.SetResult(true); await action; Assert.Equal(0, platform.Registrations);
    }
    [Fact]
    public async Task StopWhileRegistrationPending_CancelsEnableAcrossServiceInstances()
    {
        var row = await Arm(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        platform.Registering = () => { entered.SetResult(); return release.Task; };
        var starting = Service.StartAsync(row, default); await entered.Task; var stopping = Service.StopAsync();
        Assert.True(blocker.IsBlocked); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting); await stopping;
        Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(BackgroundGeofenceState.Stopped, (await journal.ReadAsync())!.State);
    }
    [Fact]
    public async Task BootstrapSampleBeforeStart_DoesNotProvideFreshOutsideEvidence()
    {
        var row = await Arm(); position.Fix = position.Fix with { CapturedUtc = client.Clock.Now.AddSeconds(-1) };
        await Service.StartAsync(row, default); var run = (await journal.ReadAsync())!; Assert.False(run.OutsideObserved);
        Inside(); await Process(run, NativeGeofenceTransition.Entered); Assert.Empty(notifications.Shown);
    }
    [Fact]
    public async Task NotificationRevocation_StopsRunOnMetadataReconciliation()
    {
        var row = await Arm(); await Service.StartAsync(row, default); notifications.Permission = ReminderPermission.Denied;
        await Service.ReconcileAsync(default); Assert.True(blocker.IsBlocked); Assert.Null(platform.Registered); Assert.Equal(1, position.Reads);
    }
    private sealed class DelayedSource(ILocationTaskSource source, TaskCompletionSource entered, TaskCompletionSource release) : ILocationTaskSource
    {
        public bool CanAccess(string scope) => source.CanAccess(scope);
        public async Task<IReadOnlyList<LocationTask>> ReadAsync(string scope) { entered.SetResult(); await release.Task; return await source.ReadAsync(scope); }
    }
    [Fact]
    public async Task PageDepartureDuringMetadataCheck_DoesNotRevokeCompletedBackgroundRun()
    {
        var row = await Arm(); await Service.StartAsync(row, default);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var delayed = new LocationReminderService(places, new DelayedSource(source, entered, release), position, notifications, client.Session, client.Clock);
        var review = new BackgroundGeofenceService(journal, delayed, platform, blocker, lease, client.Clock);
        using var cancellation = new CancellationTokenSource(); var pending = review.ReconcileAsync(cancellation.Token);
        await entered.Task; cancellation.Cancel(); release.SetResult(); await pending;
        Assert.False(blocker.IsBlocked); Assert.NotNull(platform.Registered); Assert.Equal(BackgroundGeofenceState.Active, (await journal.ReadAsync())!.State);
    }
}
