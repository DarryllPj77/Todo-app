using System.Net;
using System.Text.Json;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_sync_engine_" + Guid.NewGuid().ToString("N"));
    private SqliteSyncStore Store => new(Path.Combine(directory, "sync.db3"));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static string Partition(ClientHarness h) => SqliteSyncStore.Partition(h.Session.Current!.Origin, h.Account.User.Id);

    [Fact]
    public async Task LostReply_RetainsOperationAndBackoffThenRetriesTheSamePayload()
    {
        using var h = new ClientHarness(); await h.LoginAsync();
        var id = Guid.NewGuid(); var partition = Partition(h);
        await Store.QueueAsync(partition, id, new("Retried safely"));
        var pending = (await Store.ReadAsync(partition)).Rows[id].Pending!;
        h.Handler.Response = (_, _) => throw new HttpRequestException("Lost reply");
        var engine = new SyncEngine(h.Session, h.Api, Store, h.Clock);
        await Assert.ThrowsAsync<ApiClientException>(() => engine.RunAsync());
        Assert.Equal(pending, (await Store.ReadAsync(partition)).Rows[id].Pending);
        h.Handler.Response = (r, _) => Task.FromResult(r.Method == HttpMethod.Post
            ? ClientHarness.Json(new SyncResult(false, new(id, 1, false, pending.Task)))
            : ClientHarness.Json(r.RequestUri!.Query.Contains("cursor=1") ? new SyncPage([], 1, false) : new SyncPage([new(id, 1, false, pending.Task)], 1, false)));
        await engine.RunAsync(); // Backoff skips the upload but still downloads.
        Assert.NotNull((await Store.ReadAsync(partition)).Rows[id].Pending);
        await engine.RunAsync(retryNow: true);
        Assert.Null((await Store.ReadAsync(partition)).Rows[id].Pending);
        var uploads = h.Handler.Requests.Where(r => r.Uri.AbsolutePath == "/api/sync" && r.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, uploads.Length); Assert.Equal(uploads[0].Body, uploads[1].Body);
    }
    [Fact]
    public async Task AccountSwitchDuringUpload_DiscardsResponseAndRetainsOldAccountsOutbox()
    {
        using var h = new ClientHarness(); await h.LoginAsync();
        var partition = Partition(h); var id = Guid.NewGuid(); await Store.QueueAsync(partition, id, new("Alice"));
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Handler.Response = async (_, _) => { arrived.SetResult(); await reply.Task; return ClientHarness.Json(new SyncResult(false, new(id, 1, false, new("Alice")))); };
        var running = new SyncEngine(h.Session, h.Api, Store, h.Clock).RunAsync();
        await arrived.Task; await h.Session.SignOutAsync(); reply.SetResult();
        await Assert.ThrowsAsync<ApiClientException>(() => running);
        Assert.NotNull((await Store.ReadAsync(partition)).Rows[id].Pending);
    }
    [Fact]
    public async Task OfflineEditor_QueuesEditsAndLogoutHidesRowsWithoutDeletingThem()
    {
        using var h = new ClientHarness(); await h.LoginAsync();
        var partition = Partition(h);
        h.Handler.Response = (_, _) => throw new HttpRequestException();
        using var vm = new SyncWorkspaceViewModel(h.Session, Store, new(h.Session, h.Api, Store, h.Clock), new ClientNavigation());
        await vm.InitializeAsync(); vm.Title = "Offline draft";
        await vm.SaveCommand.ExecuteAsync(); Assert.Single(vm.Tasks);
        await vm.SyncCommand.ExecuteAsync(); Assert.NotEmpty(vm.Error);
        await h.Session.SignOutAsync(); Assert.Empty(vm.Tasks);
        Assert.Single((await Store.ReadAsync(partition)).Rows);
    }
    [Fact]
    public async Task AutoSync_PreservesUnsubmittedEditorDraftAndStaleSaveIsRejected()
    {
        using var h = new ClientHarness(); await h.LoginAsync();
        var id = Guid.NewGuid(); var partition = Partition(h);
        await Store.ApplyPageAsync(partition, 0, new([new(id, 1, false, new("Original"))], 1, false));
        using var vm = new SyncWorkspaceViewModel(h.Session, Store, new(h.Session, h.Api, Store, h.Clock), new ClientNavigation());
        await vm.InitializeAsync(); vm.SelectedTask = Assert.Single(vm.Tasks); vm.Title = "Unsubmitted draft";
        h.Handler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new SyncPage([new(id, 2, false, new("Remote edit"))], 2, false)));
        await vm.SyncCommand.ExecuteAsync(); Assert.Equal("Unsubmitted draft", vm.Title);
        await vm.SaveCommand.ExecuteAsync(); Assert.Contains("changed", vm.Error);
        Assert.Equal("Remote edit", (await Store.ReadAsync(partition)).Rows[id].Local!.Title);
    }
    [Fact]
    public async Task Unauthorized_DoesNotRemoveTheDurableOutbox()
    {
        using var h = new ClientHarness(); await h.LoginAsync();
        var partition = Partition(h); var id = Guid.NewGuid(); await Store.QueueAsync(partition, id, new("Keep me"));
        h.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiClientException>(() => new SyncEngine(h.Session, h.Api, Store, h.Clock).RunAsync());
        Assert.Null(h.Session.Current); Assert.NotNull((await Store.ReadAsync(partition)).Rows[id].Pending);
    }
}
