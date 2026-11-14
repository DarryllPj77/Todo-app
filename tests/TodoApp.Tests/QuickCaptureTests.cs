using Microsoft.Data.Sqlite;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class QuickCaptureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoCapture-" + Guid.NewGuid());
    private string PathFor(string name) => Path.Combine(directory, name);
    private SqliteLocalTaskService Device => new(PathFor("device.db3"));
    private SqliteSyncStore Sync => new(PathFor("sync.db3"));
    private QuickCaptureService Service(ClientHarness client) => new(Device, Sync, client.Session, client.Clock);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitleUsesDomainValidation(string title) => Assert.Throws<ArgumentException>(() => new QuickCaptureDraft(title));

    [Fact]
    public void OversizedTextUsesDomainValidation()
    {
        Assert.Throws<ArgumentException>(() => new QuickCaptureDraft(new string('a', 201)));
        Assert.Throws<ArgumentException>(() => new QuickCaptureDraft("title", new string('a', 4001)));
    }

    [Fact]
    public async Task DeviceCaptureNormalizesAndUsesOrdinaryDefaultsWithoutReadingTokens()
    {
        using var client = new ClientHarness();
        client.Storage.FailRead = true;
        var vm = new QuickCaptureViewModel(Service(client));
        Assert.True(await vm.SaveAsync("  Remember this  ", " notes "));
        var task = Assert.Single(await Device.GetTasksAsync());
        Assert.Equal("Remember this", task.Title); Assert.Equal("notes", task.Description);
        Assert.Equal(TaskPriority.Normal, task.Priority); Assert.False(task.IsCompleted);
        Assert.Null(task.DueDate); Assert.Null(task.CategoryId); Assert.Null(task.ParentTaskId);
        Assert.False(task.Recurrence.IsActive); Assert.Equal(0, client.Storage.ReadCount);
        Assert.Empty(client.Handler.Requests);
    }

    [Fact]
    public async Task DeviceRetryAfterReopenDoesNotOverwriteEditsOrResurrectDeletion()
    {
        var id = Guid.NewGuid(); var draft = new QuickCaptureDraft("Capture");
        Assert.False((await Device.CaptureAsync(id, draft)).AlreadyCaptured);
        await Device.UpdateAsync(id, new TaskDetails("Edited"));
        Assert.True((await Device.CaptureAsync(id, draft)).AlreadyCaptured);
        Assert.Equal("Edited", (await Device.GetByIdAsync(id))!.Title);
        await Device.DeleteAsync(id);
        Assert.True((await Device.CaptureAsync(id, draft)).AlreadyCaptured);
        Assert.Empty(await Device.GetTasksAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Device.CaptureAsync(id, new("Different")));
    }

    [Fact]
    public async Task ConcurrentDeviceRetriesCommitOneTask()
    {
        var id = Guid.NewGuid(); var draft = new QuickCaptureDraft("one");
        var results = await Task.WhenAll(Device.CaptureAsync(id, draft), Device.CaptureAsync(id, draft));
        Assert.Single(results, r => !r.AlreadyCaptured);
        Assert.Single(await Device.GetTasksAsync());
    }

    [Fact]
    public async Task DeviceReceiptFailureRollsBackTheTask()
    {
        await Device.GetTasksAsync();
        Sql("device.db3", "CREATE TRIGGER FailCapture BEFORE INSERT ON QuickCaptureReceipts BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<SqliteException>(() => Device.CaptureAsync(id, new("one")));
        Assert.Empty(await Device.GetTasksAsync());
        Sql("device.db3", "DROP TRIGGER FailCapture;");
        Assert.False((await Device.CaptureAsync(id, new("one"))).AlreadyCaptured);
    }

    [Fact]
    public async Task AccountCaptureQueuesToPinnedPartitionWithoutHttpOrDeviceWrites()
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var count = client.Handler.Requests.Count;
        var service = Service(client); var destination = await service.SelectAccountAsync();
        var id = Guid.NewGuid(); await service.SaveAsync(id, new("account draft"), destination);
        var row = Assert.Single((await Sync.ReadAsync(destination.Partition!)).Rows).Value;
        Assert.Equal(id, row.Id); Assert.Equal("account draft", row.Pending!.Task!.Title);
        Assert.Equal(0, row.Pending.BaseRevision); Assert.False(row.Pending.Deleted);
        Assert.Empty(await Device.GetTasksAsync()); Assert.Equal(count, client.Handler.Requests.Count);
        Assert.Empty((await Sync.ReadAsync("other-account")).Rows);
    }

    [Fact]
    public async Task RestoredUnverifiedAccountCanQueueOffline()
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var restored = new ClientSession(client.Settings, client.Storage, client.Clock);
        var service = new QuickCaptureService(Device, Sync, restored, client.Clock);
        var count = client.Handler.Requests.Count;
        var destination = await service.SelectAccountAsync();
        Assert.False(restored.IsVerified);
        await service.SaveAsync(Guid.NewGuid(), new("offline"), destination);
        Assert.Single((await Sync.ReadAsync(destination.Partition!)).Rows);
        Assert.Equal(count, client.Handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedOrExpiredSessionCannotWriteSelectedPartition(bool expire)
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var service = Service(client); var destination = await service.SelectAccountAsync();
        if (expire) client.Clock.Now = client.Account.ExpiresAt;
        else await client.Session.SignOutAsync();
        await Assert.ThrowsAsync<ApiClientException>(() => service.SaveAsync(Guid.NewGuid(), new("private draft"), destination));
        var state = await Sync.ReadAsync(destination.Partition!);
        Assert.Empty(state.Rows); Assert.Empty(state.CaptureReceipts);
    }

    [Fact]
    public async Task AccountAuthorizationIsCheckedAgainBeforeCommitAndRollsBack()
    {
        var checks = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sync.CaptureAsync("a", Guid.NewGuid(), new("draft"), () =>
        {
            if (++checks == 2) throw new InvalidOperationException("account changed");
        }));
        var state = await Sync.ReadAsync("a"); Assert.Empty(state.Rows); Assert.Empty(state.CaptureReceipts);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task AccountRetryKeepsOutgoingOperationAndLaterDeletion()
    {
        var id = Guid.NewGuid(); var draft = new QuickCaptureDraft("one");
        await Sync.CaptureAsync("a", id, draft, () => { });
        var outgoing = (await Sync.ReadAsync("a")).Rows[id].Pending;
        await Sync.CaptureAsync("a", id, draft, () => { });
        Assert.Equal(outgoing, (await Sync.ReadAsync("a")).Rows[id].Pending);
        await Sync.QueueAsync("a", id, null);
        Assert.True((await Sync.CaptureAsync("a", id, draft, () => { })).AlreadyCaptured);
        Assert.Null((await Sync.ReadAsync("a")).Rows[id].Local);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sync.CaptureAsync("a", id, new("changed"), () => { }));
    }

    [Fact]
    public async Task SameCaptureIdIsIsolatedByAccountPartition()
    {
        var id = Guid.NewGuid();
        await Sync.CaptureAsync("a", id, new("a draft"), () => { });
        await Sync.CaptureAsync("b", id, new("b draft"), () => { });
        Assert.Equal("a draft", (await Sync.ReadAsync("a")).Rows[id].Local!.Title);
        Assert.Equal("b draft", (await Sync.ReadAsync("b")).Rows[id].Local!.Title);
    }

    [Fact]
    public async Task ValidationRemainsEditableAndRepeatedSaveDoesNotDuplicate()
    {
        using var client = new ClientHarness(); var vm = new QuickCaptureViewModel(Service(client));
        Assert.False(await vm.SaveAsync(" ", null)); Assert.True(vm.CanEdit); Assert.NotEmpty(vm.Error);
        Assert.True(await vm.SaveAsync("valid", null)); Assert.False(vm.CanEdit);
        Assert.True(await vm.SaveAsync("other", null));
        Assert.Equal("valid", Assert.Single(await Device.GetTasksAsync()).Title);
    }

    [Fact]
    public async Task UncertainWriteRetryFreezesDraftAndDestination()
    {
        using var client = new ClientHarness(); await Device.GetTasksAsync();
        Sql("device.db3", "CREATE TRIGGER FailCapture BEFORE INSERT ON QuickCaptureReceipts BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        var vm = new QuickCaptureViewModel(Service(client));
        Assert.False(await vm.SaveAsync("original", null)); Assert.False(vm.CanEdit);
        await vm.SelectAccountAsync(); Assert.Null(vm.Destination.Partition);
        Sql("device.db3", "DROP TRIGGER FailCapture;");
        Assert.True(await vm.SaveAsync("replacement", null));
        Assert.Equal("original", Assert.Single(await Device.GetTasksAsync()).Title);
    }

    [Fact]
    public async Task CancelAndFailedAccountSelectionDoNotWriteOrReadTokensForDevice()
    {
        using var client = new ClientHarness(); var vm = new QuickCaptureViewModel(Service(client));
        await vm.SelectAccountAsync(); Assert.NotEmpty(vm.Error); Assert.Null(vm.Destination.Partition);
        vm.Close(); Assert.False(await vm.SaveAsync("discarded", null));
        Assert.Empty(await Device.GetTasksAsync()); Assert.Empty(client.Handler.Requests);
    }

    [Fact]
    public async Task VersionSixDeviceAndSyncMigrateWithoutLosingTasks()
    {
        var task = await Device.CreateAsync(new TaskDetails("existing"));
        Sql("device.db3", "DROP TABLE QuickCaptureReceipts; PRAGMA user_version=6;");
        await Device.CaptureAsync(Guid.NewGuid(), new("new"));
        Assert.Equal("existing", (await Device.GetByIdAsync(task.Id))!.Title);
        await Sync.QueueAsync("a", task.Id, new SyncTaskData("existing"));
        Sql("sync.db3", "UPDATE ReplicaPartitions SET State=json_remove(State,'$.CaptureReceipts'); PRAGMA user_version=6;");
        await Sync.CaptureAsync("a", Guid.NewGuid(), new("new"), () => { });
        Assert.Equal(2, (await Sync.ReadAsync("a")).Rows.Count);
    }

    private void Sql(string name, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={PathFor(name)};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
