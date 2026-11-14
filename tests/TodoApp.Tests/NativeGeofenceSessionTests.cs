using System.Security.Cryptography;
using TodoApp.Domain.Location;
using TodoApp.Domain.Recurrence;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class NativeGeofenceSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_geofence_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly InMemoryLocalTaskService tasks = new();
    private readonly Native platform = new();
    private readonly Position position = new();
    private readonly Notifications notifications = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly SqliteLocationReminderStore store;
    private readonly LocationReminderService reminders;
    private readonly GeofenceSessionService native;
    public NativeGeofenceSessionTests()
    {
        store = new(Path.Combine(directory, "location.db3"), new Keys());
        var source = new LocationTaskCatalog(tasks, new(Path.Combine(directory, "sync.db3")), client.Session, client.Clock);
        reminders = new(store, source, position, notifications, client.Session, client.Clock);
        native = new(reminders, platform, client.Session, client.Clock); Outside();
    }
    public void Dispose() { native.Dispose(); client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private void Outside() => position.Fix = new(0, .02, 10, client.Clock.Now);
    private void Inside() => position.Fix = new(0, 0, 10, client.Clock.Now);
    private async Task<LocationReminder> Arm()
    {
        var task = await tasks.CreateAsync(new("Native task"));
        return await reminders.ArmAsync("device", null, new(task.Id, "Place", 0, 0, 200), default);
    }
    private Task Start(Guid? page = null) => native.StartAsync(page ?? owner, "device", default);
    private Task Report(LocationReminder row, NativeGeofenceTransition transition, PositionFix? fix = null) =>
        platform.Callback!(new(row.Id, row.ArmId, transition, fix ?? position.Fix));
    private sealed class Keys : ILocationEncryptionKeyStore
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public Task<byte[]> GetAsync(bool allowCreate) => Task.FromResult(key.ToArray());
    }
    private sealed class Position : IForegroundLocationProvider
    {
        public PositionFix Fix = null!;
        public int Reads;
        public Task<PositionFix> ReadAsync(CancellationToken cancellationToken) { Reads++; return Task.FromResult(Fix); }
    }
    private sealed class Notifications : IReminderPlatform
    {
        public bool SupportsScheduling => false;
        public string DeliveryDescription => "Test";
        public ReminderPermission Permission = ReminderPermission.Allowed;
        public List<bool> Requests = new();
        public List<int> Shown = new();
        public bool Fail;
        public Task<ReminderPermission> PermissionAsync(bool request) { Requests.Add(request); return Task.FromResult(Permission); }
        public Task ScheduleAsync(int id, DateTimeOffset when) => throw new InvalidOperationException();
        public Task ShowAsync(int id) { Shown.Add(id); if (Fail) throw new IOException("Native delivery uncertain"); return Task.CompletedTask; }
        public Task CancelAsync(int id) => Task.CompletedTask;
    }
    private sealed class Native : INativeGeofencePlatform
    {
        public bool Supported = true;
        public bool IsSupported => Supported;
        public string Description => "Test native capability";
        public int Starts, Stops;
        public bool FailStart, FailStop;
        public IReadOnlyList<NativeGeofenceSpec> Regions = Array.Empty<NativeGeofenceSpec>();
        public Func<NativeGeofenceReport, Task>? Callback;
        public Func<Task>? Wait;
        public async Task StartAsync(IReadOnlyList<NativeGeofenceSpec> places, Func<NativeGeofenceReport, Task> report, CancellationToken token)
        { Starts++; Regions = places; Callback = report; if (Wait is not null) await Wait(); if (FailStart) throw new InvalidOperationException("Native denied"); }
        public Task StopAsync() { Stops++; if (FailStop) throw new IOException("Cleanup failed"); Regions = Array.Empty<NativeGeofenceSpec>(); return Task.CompletedTask; }
    }
    [Theory]
    [InlineData(100, 50, 200)] [InlineData(200, 100, 350)] [InlineData(5000, 2500, 5550)]
    public void NativeRegions_LeaveRoomForAccuracyAndOutsideMargin(int radius, double inner, double outer)
    {
        var spec = new NativeGeofenceSpec(Guid.NewGuid(), Guid.NewGuid(), 0, 0, radius);
        Assert.Equal(inner, spec.EntryRadius); Assert.Equal(outer, spec.ExitRadius);
    }
    [Fact]
    public async Task UnsupportedBuild_DoesNotReadStorageLocationOrPermissions()
    {
        platform.Supported = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start());
        Assert.Equal(0, position.Reads); Assert.Empty(notifications.Requests); Assert.False(Directory.Exists(directory)); Assert.Equal(0, platform.Starts);
    }
    [Fact]
    public async Task OutsideBootstrapThenEntry_ConsumesOnceAndStopsAllNativeRegions()
    {
        var row = await Arm(); await Start(); Assert.True(native.IsActiveFor(owner)); Assert.Single(platform.Regions);
        Inside(); await Report(row, NativeGeofenceTransition.Entered); await Report(row, NativeGeofenceTransition.Entered);
        Assert.Single(notifications.Shown); Assert.Empty(platform.Regions); Assert.False(native.IsActiveFor(owner));
        Assert.Equal(ArrivalState.Attempted, Assert.Single(await store.ReadAsync("device")).State);
        Assert.Equal(1, position.Reads); Assert.All(notifications.Requests, request => Assert.False(request));
    }
    [Fact]
    public async Task StartingInside_IgnoresOldOutsideEvidenceUntilFreshExit()
    {
        var row = await Arm(); await reminders.CheckAsync("device", default); Inside(); await Start();
        await Report(row, NativeGeofenceTransition.Entered); Assert.Empty(notifications.Shown);
        Outside(); await Report(row, NativeGeofenceTransition.Exited); Inside(); await Report(row, NativeGeofenceTransition.Entered);
        Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task InitiallyInsideNewArm_DoesNotNotifyWithoutExit()
    {
        var row = await Arm(); Inside(); await Start(); await Report(row, NativeGeofenceTransition.Entered);
        Assert.Empty(notifications.Shown); Assert.True(native.IsActiveFor(owner));
        Assert.Equal(ArrivalState.Armed, Assert.Single(await store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task StoppedOrReplacedSession_RejectsLateCallbacksIncludingFaults()
    {
        var row = await Arm(); await Start(); var old = platform.Callback!; await native.StopAsync(owner); await Start(); Inside();
        await old(new(row.Id, row.ArmId, NativeGeofenceTransition.Entered, position.Fix));
        await old(new(Guid.Empty, Guid.Empty, NativeGeofenceTransition.Unavailable, null));
        Assert.Empty(notifications.Shown); Assert.True(native.IsActiveFor(owner));
        await Report(row, NativeGeofenceTransition.Entered); Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task AnotherWindow_CannotReplaceOrStopActiveOwner()
    {
        await Arm(); await Start(); var other = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(other)); await native.StopAsync(other);
        Assert.True(native.IsActiveFor(owner)); Assert.Equal(1, platform.Starts); Assert.Single(platform.Regions);
    }
    [Theory]
    [InlineData(0)] [InlineData(11)]
    public async Task ExplicitQuota_RefusesEmptyOrOversizedWorkspace(int count)
    {
        for (var index = 0; index < count; index++) await Arm();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start()); Assert.Equal(0, platform.Starts); Assert.Equal(0, position.Reads);
    }
    [Theory]
    [InlineData(ReminderPermission.Denied)] [InlineData(ReminderPermission.Unavailable)]
    public async Task NotificationsUnavailable_NoNativeRegistrationOrLocationRead(ReminderPermission permission)
    {
        await Arm(); notifications.Permission = permission;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start()); Assert.Equal(0, platform.Starts); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task NativeRegistrationFailure_CleansPartialRegionsWithoutSampling()
    {
        await Arm(); platform.FailStart = true; await Assert.ThrowsAsync<InvalidOperationException>(() => Start());
        Assert.Empty(platform.Regions); Assert.False(native.IsActiveFor(owner)); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task CleanupFailure_BlocksCallbacksUntilExplicitStopRetry()
    {
        var row = await Arm(); await Start(); platform.FailStop = true;
        await Assert.ThrowsAsync<IOException>(() => native.StopAsync(owner)); Inside(); await Report(row, NativeGeofenceTransition.Entered);
        Assert.Empty(notifications.Shown); Assert.False(native.IsActiveFor(owner));
        platform.FailStop = false; await native.StopAsync(owner); Assert.Empty(platform.Regions);
    }
    [Theory]
    [InlineData("complete")] [InlineData("delete")] [InlineData("repeat")]
    public async Task TaskChanges_StopOnReconcileWithoutAnotherLocationRequest(string change)
    {
        var row = await Arm(); await Start();
        if (change == "complete") await tasks.SetCompletedAsync(row.TaskId, true);
        else if (change == "delete") await tasks.DeleteAsync(row.TaskId);
        else await tasks.SetRecurrenceRuleAsync(row.TaskId, new(RepeatFrequency.Daily, 1, new(2026, 11, 14)));
        await native.ReconcileAsync(owner); Assert.False(native.IsActiveFor(owner)); Assert.Empty(platform.Regions);
        Assert.Equal(1, position.Reads); Assert.Equal(ArrivalState.TaskChanged, Assert.Single(await store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task ReplacementArm_RejectsOldNativeIdentityAndPreservesNewArm()
    {
        var row = await Arm(); await Start(); var current = Assert.Single(await store.ReadAsync("device"));
        var replacement = await reminders.ArmAsync("device", current, new(row.TaskId, "Replacement", 0, 0, 200), default);
        Inside(); await Report(row, NativeGeofenceTransition.Entered); Assert.Empty(notifications.Shown);
        Assert.False(native.IsActiveFor(owner)); Assert.Equal(replacement, Assert.Single(await store.ReadAsync("device")));
    }
    [Fact]
    public async Task ForeignReport_CannotTargetAnotherReminder()
    {
        var row = await Arm(); await Start(); Inside();
        await platform.Callback!(new(Guid.NewGuid(), row.ArmId, NativeGeofenceTransition.Entered, position.Fix));
        await platform.Callback!(new(row.Id, Guid.NewGuid(), NativeGeofenceTransition.Entered, position.Fix));
        Assert.Empty(notifications.Shown); Assert.True(native.IsActiveFor(owner));
    }
    [Theory]
    [InlineData("stale")] [InlineData("mock")] [InlineData("accuracy")] [InlineData("boundary")] [InlineData("future")]
    public async Task UnreliableNativeEntry_DoesNotConsumeArm(string kind)
    {
        var row = await Arm(); await Start(); Inside();
        var fix = kind switch
        {
            "stale" => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(-3) },
            "mock" => position.Fix with { Mocked = true },
            "accuracy" => position.Fix with { AccuracyMeters = 101 },
            "boundary" => position.Fix with { Longitude = .0018 },
            _ => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(1) }
        };
        await Report(row, NativeGeofenceTransition.Entered, fix); Assert.Empty(notifications.Shown); Assert.True(native.IsActiveFor(owner));
        Assert.Equal(ArrivalState.OutsideObserved, Assert.Single(await store.ReadAsync("device")).State);
    }
    [Theory]
    [InlineData(NativeGeofenceTransition.Removed)] [InlineData(NativeGeofenceTransition.Unavailable)]
    public async Task NativeRemovalOrLossOfLocation_StopsWithoutAutoRegistration(NativeGeofenceTransition transition)
    {
        var row = await Arm(); await Start(); await Report(row, transition);
        Assert.False(native.IsActiveFor(owner)); Assert.Empty(platform.Regions); Assert.Equal(1, platform.Starts); Assert.Empty(notifications.Shown);
    }
    [Fact]
    public async Task ExpiryStopsSession_AndNeverAutomaticallyRestarts()
    {
        await Arm(); await Start(); client.Clock.Now = client.Clock.Now.AddMinutes(30); await native.ReconcileAsync(owner); await native.ReconcileAsync(owner);
        Assert.False(native.IsActiveFor(owner)); Assert.Empty(platform.Regions); Assert.Equal(1, platform.Starts); Assert.Equal(1, position.Reads);
    }
    [Fact]
    public async Task NativeShowFailure_ConsumesAttemptThenStopsAndCannotRepeat()
    {
        var row = await Arm(); await Start(); notifications.Fail = true; Inside(); await Report(row, NativeGeofenceTransition.Entered);
        Assert.False(native.IsActiveFor(owner)); Assert.Empty(platform.Regions); Assert.Single(notifications.Shown);
        Assert.Equal(ArrivalState.Attempted, Assert.Single(await store.ReadAsync("device")).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start()); Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task SignOutImmediatelyBlocksReportsAndCleansRegions()
    {
        await client.LoginAsync(); var row = await Arm(); await Start(); await client.Session.SignOutAsync(); Inside();
        await Report(row, NativeGeofenceTransition.Entered); await native.StopAsync(owner);
        Assert.Empty(notifications.Shown); Assert.Empty(platform.Regions); Assert.False(native.IsActiveFor(owner));
    }
    [Fact]
    public async Task SignOutDuringNativePermission_BlocksStartAndQueuedWork()
    {
        await client.LoginAsync(); await Arm(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        platform.Wait = () => { entered.SetResult(); return release.Task; };
        var first = Start(); await entered.Task; var queued = Start(Guid.NewGuid()); await client.Session.SignOutAsync(); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAsync<ApiClientException>(() => queued); await native.StopAsync(owner);
        Assert.Equal(1, platform.Starts); Assert.Equal(0, position.Reads); Assert.Empty(platform.Regions);
    }
    [Fact]
    public async Task PageCancellationDuringStart_CleansNativeRegistration()
    {
        await Arm(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        platform.Wait = () => { entered.SetResult(); return release.Task; };
        using var cancellation = new CancellationTokenSource(); var pending = native.StartAsync(owner, "device", cancellation.Token);
        await entered.Task; cancellation.Cancel(); release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(platform.Regions); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task ViewModelConfirmationOrWorkspaceChange_CannotStartNativeMonitoring()
    {
        await Arm(); var navigation = new ClientNavigation { Confirm = false };
        using var vm = new LocationRemindersViewModel(reminders, client.Session, navigation, native);
        await vm.StartNativeCommand.ExecuteAsync(); Assert.Equal(0, platform.Starts);
        var answer = new TaskCompletionSource<bool>(); navigation.LocationConfirmation = () => answer.Task;
        var action = vm.StartNativeCommand.ExecuteAsync(); vm.Workspace = HierarchyWorkspace.SyncedAccount; answer.SetResult(true); await action;
        Assert.Equal(0, platform.Starts); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task BootstrapFixBeforeSessionStart_CannotProvideNewOutsideEvidence()
    {
        var row = await Arm(); position.Fix = position.Fix with { CapturedUtc = client.Clock.Now.AddSeconds(-1) };
        await Start(); Inside(); await Report(row, NativeGeofenceTransition.Entered);
        Assert.Empty(notifications.Shown); Assert.Equal(ArrivalState.Armed, Assert.Single(await store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task NotificationPermissionRevoked_StopsWithoutRequestingItAgain()
    {
        await Arm(); await Start(); notifications.Permission = ReminderPermission.Denied; await native.ReconcileAsync(owner);
        Assert.False(native.IsActiveFor(owner)); Assert.Empty(platform.Regions); Assert.Equal(1, position.Reads);
        Assert.All(notifications.Requests, request => Assert.False(request));
    }
}
