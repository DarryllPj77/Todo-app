using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Tests;

public sealed class SyncStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_sync_" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(directory, "sync.db3");
    private SqliteSyncStore Store => new(PathName);
    private const string Partition = "https://one.invalid/|alice";
    private static SyncTaskData Data(string title) => new(title);
    private static SyncEntry Entry(Guid id, long revision, string? title) => new(id, revision, title is null, title is null ? null : Data(title));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task QueuedCreate_ReopensWithTheSameIdentityPayloadAndOperation()
    {
        var id = Guid.NewGuid();
        await Store.QueueAsync(Partition, id, Data("Offline"));
        var first = (await Store.ReadAsync(Partition)).Rows[id];
        var reopened = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.Equal(first.Pending, reopened.Pending);
        Assert.Equal(id, reopened.Pending!.TaskId);
        Assert.Equal(0, reopened.Pending.BaseRevision);
    }
    [Fact]
    public async Task AccountsAndOrigins_AreSeparateAndDoNotImportDeviceTasks()
    {
        await Store.QueueAsync(Partition, Guid.NewGuid(), Data("Private"));
        Assert.Empty((await Store.ReadAsync("https://one.invalid/|bob")).Rows);
        Assert.Empty((await Store.ReadAsync("https://two.invalid/|alice")).Rows);
        Assert.Single((await Store.ReadAsync(Partition)).Rows);
    }
    [Fact]
    public async Task EditsDuringAnUnacknowledgedCreate_PreserveItsPayloadAndChainTheLatestIntent()
    {
        var id = Guid.NewGuid();
        await Store.QueueAsync(Partition, id, Data("First"));
        var sent = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        await Store.QueueAsync(Partition, id, Data("Second"));
        Assert.Equal(sent, (await Store.ReadAsync(Partition)).Rows[id].Pending);
        await Store.AcknowledgeAsync(Partition, sent, new(false, Entry(id, 1, "First")));
        var row = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.Equal("Second", row.Local!.Title);
        Assert.Equal(1, row.Pending!.BaseRevision);
        Assert.NotEqual(sent.OperationId, row.Pending.OperationId);
        await Store.AcknowledgeAsync(Partition, row.Pending, new(false, Entry(id, 2, "Second")));
        Assert.Null((await Store.ReadAsync(Partition)).Rows[id].Pending);
    }
    [Fact]
    public async Task QueuedDeletion_SurvivesLostCreateReplyAndBecomesATombstone()
    {
        var id = Guid.NewGuid();
        await Store.QueueAsync(Partition, id, Data("Temporary"));
        var create = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        await Store.QueueAsync(Partition, id, null);
        await Store.AcknowledgeAsync(Partition, create, new(false, Entry(id, 1, "Temporary")));
        var deletion = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        Assert.True(deletion.Deleted);
        Assert.Equal(1, deletion.BaseRevision);
        await Store.AcknowledgeAsync(Partition, deletion, new(false, Entry(id, 2, null)));
        var row = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.Null(row.Local); Assert.Null(row.Pending); Assert.True(row.Remote!.Deleted);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflict_PreservesBothSidesUntilExplicitResolution(bool keepMine)
    {
        var id = Guid.NewGuid();
        await Store.ApplyPageAsync(Partition, 0, new([Entry(id, 1, "Original")], 1, false));
        await Store.QueueAsync(Partition, id, Data("Mine"));
        var pending = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        await Store.AcknowledgeAsync(Partition, pending, new(true, Entry(id, 2, "Theirs")));
        var conflict = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.True(conflict.Conflict); Assert.Equal("Mine", conflict.Local!.Title); Assert.Equal("Theirs", conflict.Remote!.Task!.Title);
        await Store.ResolveAsync(Partition, id, keepMine);
        var resolved = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.False(resolved.Conflict);
        Assert.Equal(keepMine ? "Mine" : "Theirs", resolved.Local!.Title);
        if (keepMine) Assert.Equal(2, resolved.Pending!.BaseRevision); else Assert.Null(resolved.Pending);
    }
    [Fact]
    public async Task Download_DoesNotOverwritePendingEditsAndCommitsCursorWithRows()
    {
        var id = Guid.NewGuid();
        await Store.ApplyPageAsync(Partition, 0, new([Entry(id, 1, "Original")], 1, false));
        await Store.QueueAsync(Partition, id, Data("Mine"));
        await Store.ApplyPageAsync(Partition, 1, new([Entry(id, 2, null)], 2, false));
        var state = await Store.ReadAsync(Partition);
        Assert.Equal(2, state.Cursor);
        Assert.Equal("Mine", state.Rows[id].Local!.Title);
        Assert.Equal(1, state.Rows[id].Pending!.BaseRevision);
    }
    [Fact]
    public async Task RejectedPage_RollsBackCursorAndDownloadedRows()
    {
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ApplyPageAsync(Partition, 0,
            new([Entry(id, 2, "Invalid range")], 1, false)));
        var state = await Store.ReadAsync(Partition);
        Assert.Equal(0, state.Cursor); Assert.Empty(state.Rows);
    }
    [Fact]
    public async Task FailedUpload_PersistsBoundedBackoffWithoutChangingOperation()
    {
        var id = Guid.NewGuid();
        await Store.QueueAsync(Partition, id, Data("Retry"));
        var pending = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 20; i++) await Store.FailedAsync(Partition, pending, now);
        var row = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.Equal(pending, row.Pending); Assert.Equal(now.AddSeconds(300), row.RetryAt);
    }
    [Fact]
    public async Task InvalidEditsAndStaleEditor_DoNotOverwriteSavedState()
    {
        var id = Guid.NewGuid();
        await Store.ApplyPageAsync(Partition, 0, new([Entry(id, 1, "Original")], 1, false));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.QueueAsync(Partition, id, Data(" ")));
        await Store.ApplyPageAsync(Partition, 1, new([Entry(id, 2, "Other window")], 2, false));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.QueueAsync(Partition, id, Data("Stale"), 1, Data("Original")));
        Assert.Equal("Other window", (await Store.ReadAsync(Partition)).Rows[id].Local!.Title);
    }
    [Fact]
    public async Task ConflictResolution_RejectsAChangedServerVersionInsteadOfOverwritingAnUnseenEdit()
    {
        var id = Guid.NewGuid();
        await Store.ApplyPageAsync(Partition, 0, new([Entry(id, 1, "Original")], 1, false));
        await Store.QueueAsync(Partition, id, Data("Mine"));
        var pending = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        await Store.AcknowledgeAsync(Partition, pending, new(true, Entry(id, 2, "Displayed server")));
        await Store.ApplyPageAsync(Partition, 1, new([Entry(id, 3, "Unseen newer server")], 3, false));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.ResolveAsync(Partition, id, true, 2));
        var row = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.True(row.Conflict); Assert.Null(row.Pending); Assert.Equal("Mine", row.Local!.Title);
        Assert.Equal("Unseen newer server", row.Remote!.Task!.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldReplayedReceipt_DoesNotRegressDownloadedStateOrSilentlyRebaseNewerEdits(bool editedAgain)
    {
        var id = Guid.NewGuid();
        await Store.QueueAsync(Partition, id, Data("Sent before lost reply"));
        var sent = (await Store.ReadAsync(Partition)).Rows[id].Pending!;
        if (editedAgain) await Store.QueueAsync(Partition, id, Data("More offline work"));
        await Store.ApplyPageAsync(Partition, 0, new([Entry(id, 2, "Another device changed it")], 2, false));
        await Store.AcknowledgeAsync(Partition, sent, new(false, Entry(id, 1, "Sent before lost reply")));
        var row = (await Store.ReadAsync(Partition)).Rows[id];
        Assert.Equal(2, row.Remote!.Revision);
        Assert.Null(row.Pending);
        Assert.Equal(editedAgain, row.Conflict);
        Assert.Equal(editedAgain ? "More offline work" : "Another device changed it", row.Local!.Title);
    }

    [Fact]
    public async Task SeparateStoreInstances_KeepConcurrentQueuedTasks()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Store.QueueAsync(Partition, Guid.NewGuid(), Data($"Task {i}"))));
        Assert.Equal(12, (await Store.ReadAsync(Partition)).Rows.Count);
    }
}
