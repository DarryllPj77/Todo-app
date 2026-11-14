using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Location;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class LocationReminderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_location_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly Keys keys = new();
    private readonly Source source = new();
    private readonly Position position = new();
    private readonly Notifications notifications = new();
    private string FilePath => Path.Combine(directory, "location.db3");
    private SqliteLocationReminderStore Store => new(FilePath, keys);
    private LocationReminderService Service => new(Store, source, position, notifications, client.Session, client.Clock);
    private LocationTask TaskRow => source.Rows[0];
    private LocationReminderDraft Draft => new(TaskRow.Id, "Private place", 0, 0, 200);
    public LocationReminderTests()
    {
        source.Rows.Add(new(Guid.NewGuid(), "Private task", RepeatState.None));
        position.Fix = new(0, 0, 10, client.Clock.Now);
    }
    public void Dispose() { client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private Task<LocationReminder> Arm(string scope = "device") => Service.ArmAsync(scope, null, Draft, default);
    private void Outside() => position.Fix = new(0, .01, 10, client.Clock.Now);
    private void Inside() => position.Fix = new(0, 0, 10, client.Clock.Now);
    private void Sql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={FilePath};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private sealed class Keys : ILocationEncryptionKeyStore
    {
        public byte[]? Value = RandomNumberGenerator.GetBytes(32);
        public Task<byte[]> GetAsync(bool allowCreate)
        {
            if (Value is null) { if (!allowCreate) throw new InvalidDataException("Key missing"); Value = RandomNumberGenerator.GetBytes(32); }
            return Task.FromResult(Value.ToArray());
        }
    }
    private sealed class Source : ILocationTaskSource
    {
        public List<LocationTask> Rows = new();
        public bool Allowed = true;
        public bool CanAccess(string scope) => Allowed;
        public Task<IReadOnlyList<LocationTask>> ReadAsync(string scope) => Task.FromResult<IReadOnlyList<LocationTask>>(Rows.ToArray());
    }
    private sealed class Position : IForegroundLocationProvider
    {
        public PositionFix Fix = null!;
        public int Reads;
        public Func<CancellationToken, Task>? Wait;
        public async Task<PositionFix> ReadAsync(CancellationToken cancellationToken)
        { Reads++; if (Wait is not null) await Wait(cancellationToken); return Fix; }
    }
    private sealed class Notifications : IReminderPlatform
    {
        public bool SupportsScheduling => false;
        public string DeliveryDescription => "Test";
        public ReminderPermission Permission = ReminderPermission.Allowed;
        public List<int> Shown = new();
        public List<int> Canceled = new();
        public List<bool> Requests = new();
        public bool FailShow, FailCancel;
        public Task<ReminderPermission> PermissionAsync(bool request) { Requests.Add(request); return Task.FromResult(Permission); }
        public Task ScheduleAsync(int id, DateTimeOffset when) => throw new InvalidOperationException("Location must never schedule a time reminder.");
        public Task ShowAsync(int id) { Shown.Add(id); if (FailShow) throw new IOException("Native show failed"); return Task.CompletedTask; }
        public Task CancelAsync(int id) { Canceled.Add(id); if (FailCancel) throw new IOException("Native cancel failed"); return Task.CompletedTask; }
    }
    [Fact]
    public void Haversine_HandlesDateLineAndPoles()
    {
        Assert.InRange(LocationRules.DistanceMeters(0, 179.999, 0, -179.999), 222, 223);
        Assert.InRange(LocationRules.DistanceMeters(90, -180, 90, 180), 0, .001);
    }
    [Theory]
    [InlineData(91, 0, 200)] [InlineData(0, 181, 200)] [InlineData(0, 0, 99)] [InlineData(0, 0, 5001)] [InlineData(double.NaN, 0, 200)]
    public void Draft_RejectsInvalidCoordinatesOrRadius(double latitude, double longitude, int radius) =>
        Assert.Throws<ArgumentException>(() => LocationRules.Validate(Draft with { Latitude = latitude, Longitude = longitude, RadiusMeters = radius }));
    [Theory]
    [InlineData("stale")] [InlineData("future")] [InlineData("mock")] [InlineData("missing")] [InlineData("inaccurate")] [InlineData("boundary")] [InlineData("invalid")]
    public async Task UnreliableFix_DoesNotAdvanceArrival(string kind)
    {
        var row = await Arm();
        var fix = kind switch
        {
            "stale" => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(-3) },
            "future" => position.Fix with { CapturedUtc = client.Clock.Now.AddMinutes(1) },
            "mock" => position.Fix with { Mocked = true },
            "missing" => position.Fix with { AccuracyMeters = null },
            "inaccurate" => position.Fix with { AccuracyMeters = 101 },
            "boundary" => position.Fix with { Longitude = .0018 },
            _ => position.Fix with { Latitude = double.NaN }
        };
        Assert.Equal(Proximity.Uncertain, LocationRules.Classify(row, fix, client.Clock.Now));
        position.Fix = fix; await Service.CheckAsync("device", default);
        Assert.Equal(ArrivalState.Armed, Assert.Single(await Store.ReadAsync("device")).State); Assert.Empty(notifications.Shown);
    }
    [Fact]
    public async Task InitialInside_DoesNotNotify_OutsideThenInsideAttemptsOnlyOnceAcrossRestart()
    {
        await Arm(); await Service.CheckAsync("device", default); Assert.Empty(notifications.Shown);
        Outside(); await Service.CheckAsync("device", default);
        Assert.Equal(ArrivalState.OutsideObserved, Assert.Single(await Store.ReadAsync("device")).State);
        Inside(); await Service.CheckAsync("device", default); await Service.CheckAsync("device", default);
        Assert.True(Assert.Single(notifications.Shown) < 0);
        Assert.Equal(ArrivalState.Attempted, Assert.Single(await Store.ReadAsync("device")).State);
        Assert.All(notifications.Requests, request => Assert.False(request));
    }
    [Fact]
    public async Task ExplicitRearm_ResetsOutsideEvidenceAndNativeIdRemainsStable()
    {
        var row = await Arm(); Outside(); await Service.CheckAsync("device", default);
        row = Assert.Single(await Store.ReadAsync("device"));
        var armed = await Service.ArmAsync("device", row, Draft, default);
        Assert.NotEqual(row.ArmId, armed.ArmId); Assert.Equal(row.NotificationId, armed.NotificationId);
        Inside(); await Service.CheckAsync("device", default); Assert.Empty(notifications.Shown);
    }
    [Theory]
    [InlineData(ReminderPermission.Denied)] [InlineData(ReminderPermission.Unavailable)]
    public async Task BlockedNotifications_DoNotReadLocationOrConsumeArm(ReminderPermission permission)
    {
        await Arm(); notifications.Permission = permission; await Service.CheckAsync("device", default);
        Assert.Equal(0, position.Reads); Assert.Equal(ArrivalState.Armed, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task NativeShowFailure_ConsumesAttemptAndDoesNotRetry()
    {
        await Arm(); Outside(); await Service.CheckAsync("device", default); Inside(); notifications.FailShow = true;
        await Assert.ThrowsAsync<IOException>(() => Service.CheckAsync("device", default)); notifications.FailShow = false;
        await Service.CheckAsync("device", default); Assert.Single(notifications.Shown);
        Assert.Equal(ArrivalState.Attempted, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task TaskMissingOrOccurrenceChanged_DisablesBeforeSampling()
    {
        await Arm(); source.Rows.Clear(); await Service.CheckAsync("device", default);
        Assert.Equal(0, position.Reads); Assert.Equal(ArrivalState.TaskChanged, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task TaskChangesWhileLocationPending_CannotNotify()
    {
        await Arm(); Outside(); await Service.CheckAsync("device", default); Inside();
        position.Wait = _ => { source.Rows.Clear(); return Task.CompletedTask; };
        await Service.CheckAsync("device", default); Assert.Empty(notifications.Shown);
        Assert.Equal(ArrivalState.TaskChanged, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task CancellationWhileLocationPending_DoesNotAdvance()
    {
        await Arm(); Outside(); using var cancel = new CancellationTokenSource();
        position.Wait = _ => { cancel.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CheckAsync("device", cancel.Token));
        Assert.Equal(ArrivalState.Armed, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task AccountChangeWhileLocationPending_DiscardsFix()
    {
        await client.LoginAsync(); await Arm("account-a"); Outside();
        position.Wait = async _ => await client.Session.SignOutAsync();
        await Assert.ThrowsAsync<ApiClientException>(() => Service.CheckAsync("account-a", default));
        Assert.Equal(ArrivalState.Armed, Assert.Single(await Store.ReadAsync("account-a")).State); Assert.Empty(notifications.Shown);
    }
    [Fact]
    public async Task DisableAndDelete_CancelAndNeverReuseNativeIds()
    {
        var row = await Arm(); await Service.RemoveAsync("device", row, true, default); Assert.Empty(await Store.ReadAsync("device"));
        var next = await Arm(); Assert.True(next.NotificationId < row.NotificationId); Assert.Contains(row.NotificationId, notifications.Canceled);
    }
    [Fact]
    public async Task FailedCancellation_RetainsDisabledMetadataForRetry()
    {
        var row = await Arm(); notifications.FailCancel = true;
        await Assert.ThrowsAsync<IOException>(() => Service.RemoveAsync("device", row, true, default));
        var disabled = Assert.Single(await Store.ReadAsync("device")); Assert.Equal(ArrivalState.Disabled, disabled.State);
        notifications.FailCancel = false; await Service.RemoveAsync("device", disabled, true, default); Assert.Empty(await Store.ReadAsync("device"));
    }
    [Fact]
    public async Task ProtectedPayload_RoundTripsWithoutPlaintextTitlePlaceOrCoordinates()
    {
        var row = await Arm(); Assert.Equal(row, Assert.Single(await Store.ReadAsync("device")));
        var bytes = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(FilePath));
        Assert.DoesNotContain("Private place", bytes); Assert.DoesNotContain("Private task", bytes); Assert.DoesNotContain("Latitude", bytes);
    }
    [Theory]
    [InlineData("UPDATE LocationReminders SET Data='v1.bad.bad.bad';")]
    [InlineData("UPDATE LocationReminders SET Scope='other';")]
    [InlineData("UPDATE LocationReminders SET Revision=Revision+1;")]
    [InlineData("UPDATE LocationMetadata SET KeyHash=NULL;")]
    public async Task Tampering_FailsClosedWithoutReset(string sql)
    {
        await Arm(); Sql(sql);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync(sql.Contains("Scope=") ? "other" : "device"));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task MissingOrDifferentKey_DoesNotResetData(bool missing)
    {
        await Arm(); keys.Value = missing ? null : RandomNumberGenerator.GetBytes(32);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync("device"));
        if (missing) Assert.Null(keys.Value);
    }
    [Fact]
    public async Task StaleRevisionAndCrossScopeId_CannotOverwriteExistingPlace()
    {
        var row = await Arm(); var disabled = await Store.ChangeAsync(row, ArrivalState.Disabled, "Disabled");
        await Assert.ThrowsAsync<LocationReminderConflictException>(() => Store.ArmAsync("device", row.Id, Draft, TaskRow, row.Revision));
        await Assert.ThrowsAsync<LocationReminderConflictException>(() => Store.ArmAsync("other", row.Id, Draft, TaskRow, null));
        Assert.Equal(disabled, Assert.Single(await Store.ReadAsync("device"))); Assert.Empty(await Store.ReadAsync("other"));
    }
    [Fact]
    public async Task ConcurrentChecks_UseOneAttemptAcrossServiceInstances()
    {
        await Arm(); Outside(); await Service.CheckAsync("device", default); Inside();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        { try { await Service.CheckAsync("device", default); } catch (LocationReminderConflictException) { } }));
        Assert.Single(notifications.Shown);
    }
    [Fact]
    public async Task ViewModel_CanceledConfirmationDoesNotAcquireOrSaveLocation()
    {
        var navigation = new ClientNavigation { Confirm = false }; using var vm = new LocationRemindersViewModel(Service, client.Session, navigation);
        await vm.PositionCommand.ExecuteAsync(); await vm.CheckCommand.ExecuteAsync(); Assert.Equal(0, position.Reads);
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = vm.Tasks[0]; vm.PlaceName = "Place"; vm.Latitude = vm.Longitude = "0";
        await vm.SaveCommand.ExecuteAsync(); Assert.Empty(await Store.ReadAsync("device"));
    }
    [Fact]
    public async Task WorkspaceChangeDuringConfirmation_CannotApplyOldDraft()
    {
        var answer = new TaskCompletionSource<bool>(); var navigation = new ClientNavigation { LocationConfirmation = () => answer.Task };
        using var vm = new LocationRemindersViewModel(Service, client.Session, navigation);
        var operation = vm.PositionCommand.ExecuteAsync(); vm.Workspace = HierarchyWorkspace.SyncedAccount; answer.SetResult(true); await operation;
        Assert.Equal(0, position.Reads); Assert.Empty(vm.Latitude);
    }
    [Fact]
    public async Task ActualTaskCatalog_ExcludesCompletedTasksAndAccountAccessExpires()
    {
        var device = new InMemoryLocalTaskService(); var id = (await device.CreateAsync(new("Task"))).Id;
        var catalog = new LocationTaskCatalog(device, new SqliteSyncStore(Path.Combine(directory, "sync.db3")), client.Session, client.Clock);
        Assert.Single(await catalog.ReadAsync("device")); await device.SetCompletedAsync(id, true); Assert.Empty(await catalog.ReadAsync("device"));
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
        Assert.True(catalog.CanAccess(scope)); Assert.False(catalog.CanAccess("another-account"));
        client.Clock.Now = client.Clock.Now.AddHours(1); Assert.False(catalog.CanAccess(scope));
    }
    [Fact]
    public async Task RepeatOccurrenceChange_RequiresExplicitRearm()
    {
        source.Rows[0] = TaskRow with { Recurrence = new(new(RepeatFrequency.Daily, 1, new(2026, 11, 14)), 0, 0) };
        await Arm(); Outside(); await Service.CheckAsync("device", default);
        source.Rows[0] = TaskRow with { Recurrence = TaskRow.Recurrence with { Occurrence = 1 } };
        Inside(); await Service.CheckAsync("device", default); Assert.Empty(notifications.Shown);
        Assert.Equal(ArrivalState.TaskChanged, Assert.Single(await Store.ReadAsync("device")).State);
    }
    [Fact]
    public async Task RenameRetainsCapturedTitleWithoutCompletingTask()
    {
        var captured = TaskRow.Title; await Arm(); source.Rows[0] = TaskRow with { Title = "Renamed" };
        Outside(); await Service.CheckAsync("device", default); Inside(); await Service.CheckAsync("device", default);
        Assert.Single(notifications.Shown); Assert.Equal(captured, Assert.Single(await Store.ReadAsync("device")).TaskTitle);
        Assert.Single(source.Rows);
    }
    [Fact]
    public async Task SyncedConflict_IsExcludedAndDisablesPreviouslyArmedPlace()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
        var sync = new SqliteSyncStore(Path.Combine(directory, "sync.db3")); var id = TaskRow.Id;
        await sync.ApplyPageAsync(scope, 0, new([new(id, 1, false, new("Account task"))], 1, false));
        var catalog = new LocationTaskCatalog(new InMemoryLocalTaskService(), sync, client.Session, client.Clock);
        var service = new LocationReminderService(Store, catalog, position, notifications, client.Session, client.Clock);
        await service.ArmAsync(scope, null, Draft, default);
        await sync.QueueAsync(scope, id, new("My edit"));
        var pending = (await sync.ReadAsync(scope)).Rows[id].Pending!;
        await sync.AcknowledgeAsync(scope, pending, new(true, new(id, 2, false, new("Their edit"))));
        Assert.Empty(await catalog.ReadAsync(scope));
        var snapshot = await service.ReadAsync(scope, default);
        Assert.Equal(ArrivalState.TaskChanged, Assert.Single(snapshot.Reminders).State); Assert.Equal(0, position.Reads);
    }
    [Fact]
    public async Task ScopeChangeWhileQueued_CannotAcquireAnotherLocationFix()
    {
        await client.LoginAsync(); await Arm(); Outside();
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        position.Wait = _ => { entered.TrySetResult(); return release.Task; };
        var service = Service; var first = service.CheckAsync("device", default); await entered.Task;
        var queued = service.PositionAsync("device", default); await client.Session.SignOutAsync(); release.SetResult();
        await Assert.ThrowsAsync<ApiClientException>(() => first); await Assert.ThrowsAsync<ApiClientException>(() => queued); Assert.Equal(1, position.Reads);
    }
}
