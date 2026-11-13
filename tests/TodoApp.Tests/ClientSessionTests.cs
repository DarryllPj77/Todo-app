using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed class ClientSessionTests
{
    [Fact]
    public async Task Login_BindsProtectedSessionToOriginAndNeverStoresPasswordOrGlobalAuthHeader()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        var saved = JsonSerializer.Deserialize<SavedSession>(harness.Storage.Value!)!;
        Assert.Equal(harness.Account.User, saved.User);
        Assert.Equal("https://localhost:7114/", saved.Origin);
        Assert.Equal(harness.Account.AccessToken, saved.AccessToken);
        Assert.DoesNotContain("Test password is never saved", harness.Storage.Value);
        Assert.Null(harness.Http.DefaultRequestHeaders.Authorization);
        Assert.Null(harness.Handler.Requests[0].Token);
        await harness.Api.GetTasksAsync();
        Assert.Equal(harness.Account.AccessToken, harness.Handler.Requests[1].Token);
        Assert.Equal("/api/tasks", harness.Handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task SavedSession_MustBeVerifiedBeforeWorkspaceCalls()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        var restored = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        await restored.RestoreAsync();
        Assert.NotNull(restored.Current);
        Assert.False(restored.IsVerified);
        var api = new TodoApiClient(harness.Http, restored);
        var count = harness.Handler.Requests.Count;
        await Assert.ThrowsAsync<ApiClientException>(() => api.GetTasksAsync());
        Assert.Equal(count, harness.Handler.Requests.Count);
        await api.VerifySessionAsync();
        Assert.True(restored.IsVerified);
        await api.GetTasksAsync();
    }

    [Fact]
    public async Task ExpiredSession_IsRemovedBeforeAProtectedRequestIsSent()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        harness.Clock.Now = harness.Clock.Now.AddMinutes(20);
        await Assert.ThrowsAsync<ApiClientException>(() => harness.Api.GetTasksAsync());
        Assert.Null(harness.Session.Current);
        Assert.Null(harness.Storage.Value);
        Assert.True(harness.Settings.RestorationBlocked);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task FailedSecureRemoval_StillBlocksRestorationInANewSessionInstance()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        harness.Storage.FailClear = true;
        var exception = await Assert.ThrowsAsync<ApiClientException>(harness.Session.SignOutAsync);
        Assert.Contains("Signed out locally", exception.Message);
        Assert.Null(harness.Session.Current);
        Assert.NotNull(harness.Storage.Value);
        var reads = harness.Storage.ReadCount;
        var restarted = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        await restarted.RestoreAsync();
        Assert.Null(restarted.Current);
        Assert.Equal(reads, harness.Storage.ReadCount);
        harness.Storage.FailClear = false;
        await restarted.SignOutAsync();
        Assert.Null(harness.Storage.Value);
    }

    [Fact]
    public async Task SecureWriteFailure_DoesNotCreateAnAuthenticatedMemoryOrPlaintextFallback()
    {
        using var harness = new ClientHarness();
        harness.Storage.FailWrite = true;
        await Assert.ThrowsAsync<ApiClientException>(harness.LoginAsync);
        Assert.Null(harness.Session.Current);
        Assert.Null(harness.Storage.Value);
        Assert.True(harness.Settings.RestorationBlocked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnreadableSavedSession_BlocksAutomaticRestoration(bool readFailure)
    {
        using var harness = new ClientHarness();
        harness.Storage.FailRead = readFailure;
        harness.Storage.Value = "not-session-json";
        await Assert.ThrowsAsync<ApiClientException>(harness.Session.RestoreAsync);
        Assert.Null(harness.Session.Current);
        Assert.True(harness.Settings.RestorationBlocked);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task AddressChanges_ClearSessionAndRejectStoredTokensFromAnotherOrigin()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        var oldSaved = harness.Storage.Value;
        await harness.Session.ChangeEndpointAsync("https://other.example.invalid:7443");
        Assert.Null(harness.Session.Current);
        Assert.Null(harness.Storage.Value);
        Assert.Equal("https://other.example.invalid:7443/", harness.Settings.ApiBaseUrl);
        await Assert.ThrowsAsync<ApiClientException>(() => harness.Api.GetTasksAsync());
        Assert.Single(harness.Handler.Requests);
        harness.Storage.Value = oldSaved;
        harness.Settings.RestorationBlocked = false;
        var restarted = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        await restarted.RestoreAsync();
        Assert.Null(restarted.Current);
        Assert.Null(harness.Storage.Value);
    }

    [Theory]
    [InlineData("http://localhost:5062")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid/api")]
    [InlineData("https://example.invalid/?key=secret")]
    [InlineData("https://example.invalid/#fragment")]
    [InlineData("localhost:7114")]
    public void UnsafeOrAmbiguousAddresses_AreRejected(string address) =>
        Assert.Throws<ApiClientException>(() => ClientSession.ParseEndpoint(address));

    [Fact]
    public async Task LogoutDuringRestore_DoesNotResurrectTheLoadedSession()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        harness.Storage.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        var reading = restored.RestoreAsync();
        var loggingOut = restored.SignOutAsync();
        harness.Storage.ReadGate.SetResult();
        await Task.WhenAll(reading, loggingOut);
        Assert.Null(restored.Current);
        Assert.Null(harness.Storage.Value);
        Assert.True(harness.Settings.RestorationBlocked);
    }

    [Fact]
    public async Task LogoutDuringSecureWrite_DoesNotResurrectAPersistedSession()
    {
        using var harness = new ClientHarness();
        harness.Storage.WriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = harness.LoginAsync();
        var logout = harness.Session.SignOutAsync();
        harness.Storage.WriteGate.SetResult();
        await Assert.ThrowsAsync<ApiClientException>(() => login);
        await logout;
        Assert.Null(harness.Session.Current);
        Assert.Null(harness.Storage.Value);
        Assert.True(harness.Settings.RestorationBlocked);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task HttpFailures_InvalidateOnlyRejectedSessions(HttpStatusCode code, bool clearsSession)
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        harness.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(code));
        var error = await Assert.ThrowsAsync<ApiClientException>(() => harness.Api.GetTasksAsync());
        Assert.Equal(code, error.StatusCode);
        Assert.Equal(clearsSession, harness.Session.Current is null);
        Assert.Equal(clearsSession, harness.Storage.Value is null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkAndTimeoutFailures_KeepTheSessionAndReturnSafeMessages(bool timeout)
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        harness.Handler.Response = (_, _) => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("Untrusted low-level details"));
        var error = await Assert.ThrowsAsync<ApiClientException>(() => harness.Api.GetTasksAsync());
        Assert.NotNull(harness.Session.Current);
        Assert.DoesNotContain("Untrusted low-level details", error.Message);
        Assert.Contains(timeout ? "timed out" : "reach the API", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_RemainsCancellationRatherThanAnAuthenticationFailure()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Api.GetTasksAsync(cancellationToken: cancellation.Token));
        Assert.NotNull(harness.Session.Current);
    }

    [Fact]
    public async Task InvalidSessionResponse_IsNotPersisted()
    {
        using var harness = new ClientHarness();
        harness.Account = harness.Account with { TokenType = "Unknown" };
        await Assert.ThrowsAsync<ApiClientException>(harness.LoginAsync);
        Assert.Null(harness.Session.Current);
        Assert.Null(harness.Storage.Value);
    }

    [Fact]
    public async Task OverlappingLoginAttempts_DoNotLetAnOlderAttemptReplaceTheNewerSession()
    {
        using var harness = new ClientHarness();
        harness.Storage.Value = JsonSerializer.Serialize(new SavedSession("https://localhost:7114/", "old-token",
            harness.Clock.Now.AddMinutes(15), harness.Account.User));
        harness.Storage.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        var api = new TodoApiClient(harness.Http, session);
        var restoring = session.RestoreAsync();
        var oldAttempt = api.SignInAsync("older@example.invalid", "Local test password", register: false);
        var latestAttempt = api.SignInAsync("latest@example.invalid", "Local test password", register: false);
        harness.Account = harness.Auth("latest-token", "latest@example.invalid");
        harness.Storage.ReadGate.SetResult();
        await restoring;
        await Assert.ThrowsAsync<ApiClientException>(() => oldAttempt);
        await latestAttempt;
        Assert.Equal("latest-token", session.Current!.AccessToken);
        Assert.Contains("latest@example.invalid", Assert.Single(harness.Handler.Requests).Body);
    }

    [Fact]
    public async Task MeMustMatchTheSavedAccountBeforeRestorationIsVerified()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        var restored = new ClientSession(harness.Settings, harness.Storage, harness.Clock);
        await restored.RestoreAsync();
        harness.Account = harness.Auth("other-token", "other@example.invalid");
        await Assert.ThrowsAsync<ApiClientException>(() => new TodoApiClient(harness.Http, restored).VerifySessionAsync());
        Assert.False(restored.IsVerified);
    }
}
