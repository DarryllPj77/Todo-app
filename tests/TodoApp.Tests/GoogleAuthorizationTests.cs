using System.Net;
using System.Text.Json;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class GoogleAuthorizationTests : IDisposable
{
    private const string ClientId = "12345-testing.apps.googleusercontent.com";
    private readonly ClientHarness client = new();
    private readonly Vault vault = new();
    private readonly Broker broker = new();
    private readonly ClientHandler handler = new();
    private readonly HttpClient http;
    private readonly GoogleConnectionService service;
    private int seconds = 3600;
    private string subject = "google-subject", scopes = GoogleOAuthProtocol.Scopes;
    private bool invalidRefresh, revokeFailure, rotateRefresh;
    private string Scope => TodoApp.Infrastructure.Services.SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
    public GoogleAuthorizationTests()
    {
        handler.Response = async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                if (form.Contains("grant_type=refresh_token") && invalidRefresh)
                    return ClientHarness.Json(new { error = "invalid_grant", error_description = "SECRET RESPONSE MUST NOT DISPLAY" }, HttpStatusCode.BadRequest);
                return ClientHarness.Json(new { access_token = "google-access", refresh_token = form.Contains("grant_type=refresh_token") ? (rotateRefresh ? "google-refresh-rotated" : null) : "google-refresh",
                    token_type = "Bearer", expires_in = seconds, scope = scopes });
            }
            if (request.RequestUri.AbsolutePath == "/v1/userinfo") return ClientHarness.Json(new { sub = subject, email = "google@example.invalid", email_verified = true });
            if (request.RequestUri.AbsolutePath == "/revoke") return new HttpResponseMessage(revokeFailure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            throw new InvalidOperationException("Unexpected provider endpoint");
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 65536 };
        service = new(client.Session, vault, broker, new GoogleOAuthProtocol(http, client.Clock), client.Clock);
    }
    public void Dispose() { service.Dispose(); http.Dispose(); client.Dispose(); }
    private Task<GoogleConnectionInfo> ConnectAsync() => service.ConnectAsync(ClientId, "desktop-client-secret", CancellationToken.None);

    [Fact]
    public void PkceUsesThePublishedS256VectorAndFreshRandomState()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", GoogleOAuthProtocol.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var first = GoogleOAuthProtocol.RandomValue(); Assert.Equal(43, first.Length); Assert.NotEqual(first, GoogleOAuthProtocol.RandomValue());
        Assert.DoesNotContain("=", first);
    }
    [Theory]
    [InlineData("http://127.0.0.1:54321/other?state=expected&code=code")]
    [InlineData("http://127.0.0.1:54321/oauth2/callback?state=wrong&code=code")]
    [InlineData("http://127.0.0.1:54321/oauth2/callback?state=expected&state=expected&code=code")]
    [InlineData("http://127.0.0.1:54321/oauth2/callback?state=expected&error=access_denied")]
    [InlineData("http://127.0.0.1:54321/oauth2/callback?state=expected&code=one&code=two")]
    public void UnexpectedDeniedAndDuplicateCallbacksAreRejected(string callback) => Assert.Throws<GoogleAuthorizationException>(() =>
        GoogleOAuthProtocol.CallbackCode(new(callback), new("http://127.0.0.1:54321/oauth2/callback"), "expected"));

    [Fact]
    public async Task ConnectPersistsOnlyAfterIdentityCheckAndUsesSeparateGoogleEndpoints()
    {
        await client.LoginAsync(); var info = await ConnectAsync();
        Assert.Equal("google@example.invalid", info.Email); Assert.Equal(ClientId, info.ClientId);
        var grant = JsonSerializer.Deserialize<GoogleGrant>(vault.Values[Scope])!;
        Assert.Equal("google-subject", grant.Subject); Assert.Equal(Scope, grant.Scope);
        Assert.DoesNotContain("google-access", grant.ToString()); Assert.DoesNotContain("google-refresh", grant.ToString());
        Assert.DoesNotContain("google-access", info.ToString());
        var auth = GoogleOAuthProtocol.Query(broker.Authorization!);
        Assert.Equal("S256", auth["code_challenge_method"]); Assert.Equal("offline", auth["access_type"]);
        Assert.Equal(GoogleOAuthProtocol.Scopes, auth["scope"]); Assert.DoesNotContain("desktop-client-secret", broker.Authorization!.AbsoluteUri);
        var exchange = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/token");
        Assert.Contains("code_verifier=", exchange.Body); Assert.Contains("client_secret=desktop-client-secret", exchange.Body);
        Assert.All(handler.Requests, r => Assert.Contains(r.Uri.Host, new[] { "oauth2.googleapis.com", "openidconnect.googleapis.com" }));
        Assert.All(handler.Requests, r => Assert.NotEqual(client.Account.AccessToken, r.Token));
    }
    [Fact]
    public async Task MissingOrUnverifiedTodoSessionCannotLaunchGoogle()
    {
        await Assert.ThrowsAsync<GoogleAuthorizationException>(ConnectAsync); Assert.Null(broker.Authorization); Assert.Empty(handler.Requests);
        await client.LoginAsync();
        var restored = new ClientSession(client.Settings, client.Storage, client.Clock); await restored.RestoreAsync(); Assert.False(restored.IsVerified);
        using var unverified = new GoogleConnectionService(restored, vault, broker, new GoogleOAuthProtocol(http, client.Clock), client.Clock);
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => unverified.ConnectAsync(ClientId, "", CancellationToken.None));
        Assert.Null(broker.Authorization); Assert.Empty(vault.Values);
    }
    [Fact]
    public async Task StateMismatchDoesNotExchangeOrPersistTokens()
    {
        await client.LoginAsync(); broker.WrongState = true;
        await Assert.ThrowsAsync<GoogleAuthorizationException>(ConnectAsync); Assert.Empty(handler.Requests); Assert.Empty(vault.Values);
    }
    [Fact]
    public async Task PartialScopeConsentIsRejectedWithoutSaving()
    {
        await client.LoginAsync(); scopes = "openid email";
        await Assert.ThrowsAsync<GoogleAuthorizationException>(ConnectAsync); Assert.Empty(vault.Values);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath == "/v1/userinfo");
    }
    [Fact]
    public async Task ClientSwitchRequiresExplicitDisconnect()
    {
        await client.LoginAsync(); await ConnectAsync(); var requests = handler.Requests.Count;
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.ConnectAsync("67890-other.apps.googleusercontent.com", "", CancellationToken.None));
        Assert.Equal(requests, handler.Requests.Count); Assert.Single(vault.Values);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiringTokenRefreshKeepsOrRotatesRefreshTokenAndRechecksSubject(bool rotate)
    {
        rotateRefresh = rotate;
        await client.LoginAsync(); seconds = 120; await ConnectAsync(); client.Clock.Now = client.Clock.Now.AddSeconds(90);
        Assert.NotNull(await service.CheckAsync(CancellationToken.None));
        var grant = JsonSerializer.Deserialize<GoogleGrant>(vault.Values[Scope])!;
        Assert.Equal(rotate ? "google-refresh-rotated" : "google-refresh", grant.RefreshToken); Assert.True(grant.ExpiresAt > client.Clock.Now);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("grant_type=refresh_token") == true);
    }
    [Fact]
    public async Task InvalidGrantBlocksRestorationWithoutShowingProviderResponse()
    {
        await client.LoginAsync(); seconds = 120; await ConnectAsync(); client.Clock.Now = client.Clock.Now.AddSeconds(90); invalidRefresh = true;
        var error = await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.CheckAsync(CancellationToken.None));
        Assert.DoesNotContain("SECRET RESPONSE", error.Message); Assert.Empty(vault.Values); Assert.Contains(Scope, vault.Blocked);
        Assert.Null(await service.CheckAsync(CancellationToken.None));
    }
    [Fact]
    public async Task ChangedGoogleSubjectCannotSilentlyReplaceConnectedIdentity()
    {
        await client.LoginAsync(); await ConnectAsync(); subject = "different-google-subject";
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.CheckAsync(CancellationToken.None));
        Assert.Empty(vault.Values);
    }
    [Fact]
    public async Task TodoLogoutDuringConsentDiscardsLateCallback()
    {
        await client.LoginAsync(); broker.BeforeReturn = () => client.Session.SignOutAsync();
        await Assert.ThrowsAnyAsync<Exception>(ConnectAsync); Assert.Empty(handler.Requests); Assert.Empty(vault.Values);
    }
    [Fact]
    public async Task SecureSaveFailureCannotRestoreAnOldGrant()
    {
        await client.LoginAsync(); vault.FailWrite = true;
        await Assert.ThrowsAsync<IOException>(ConnectAsync);
        Assert.Contains(Scope, vault.Blocked); Assert.Null(await service.CheckAsync(CancellationToken.None));
    }
    [Fact]
    public async Task TodoAccountExpiryAndSwitchNeverExposeAnotherGrant()
    {
        await client.LoginAsync(); await ConnectAsync(); var firstScope = Scope;
        client.Clock.Now = client.Session.Current!.ExpiresAt.AddSeconds(1);
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.CheckAsync(CancellationToken.None));
        client.Account = client.Auth("second-todo-token", "second@example.invalid"); await client.LoginAsync();
        Assert.Null(await service.CheckAsync(CancellationToken.None)); Assert.Contains(firstScope, vault.Values.Keys);
    }
    [Fact]
    public async Task LocalDisconnectDoesNotSendRevocationAndRemoteFailureRetainsRetryCredentials()
    {
        await client.LoginAsync(); await ConnectAsync(); revokeFailure = true;
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.RevokeAsync(CancellationToken.None)); Assert.Single(vault.Values);
        var prior = handler.Requests.Count; await service.ForgetAsync(CancellationToken.None);
        Assert.Equal(prior, handler.Requests.Count); Assert.Empty(vault.Values); Assert.Null(await service.CheckAsync(CancellationToken.None));
    }
    [Fact]
    public async Task ConfirmedRevocationUsesFormBodyAndBlocksLocalGrant()
    {
        await client.LoginAsync(); await ConnectAsync(); await service.RevokeAsync(CancellationToken.None);
        var request = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/revoke");
        Assert.Equal("", request.Uri.Query); Assert.Contains("token=google-refresh", request.Body); Assert.Empty(vault.Values);
    }
    [Fact]
    public async Task FailedPersistentBlockStaysFailClosedInThisProcess()
    {
        await client.LoginAsync(); await ConnectAsync(); vault.FailBlock = true;
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.ForgetAsync(CancellationToken.None));
        Assert.Null(await service.CheckAsync(CancellationToken.None));
    }
    [Fact]
    public async Task MalformedProtectedDataIsBlockedWithoutProviderCalls()
    {
        await client.LoginAsync(); vault.Values[Scope] = "{ broken";
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => service.CheckAsync(CancellationToken.None));
        Assert.Empty(handler.Requests); Assert.Contains(Scope, vault.Blocked);
    }
    [Fact]
    public async Task ViewModelRequiresConfirmationAndAccountChangeCancelsDisconnect()
    {
        await client.LoginAsync(); await ConnectAsync(); var originalScope = Scope;
        var navigation = new ClientNavigation(); using var vm = new GoogleConnectionViewModel(service, client.Session, navigation);
        await vm.CheckCommand.ExecuteAsync(); Assert.Contains("google@example.invalid", vm.ConnectionLabel);
        await vm.ForgetCommand.ExecuteAsync(); Assert.Single(vault.Values);
        navigation.GoogleConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.ForgetCommand.ExecuteAsync(); Assert.DoesNotContain("google@example.invalid", vm.ConnectionLabel);
        Assert.Contains(originalScope, vault.Values.Keys);
    }
    [Fact]
    public async Task RealLoopbackRejectsWrongStateThenAcceptsOnlyMatchingCallback()
    {
        Task? browserWork = null;
        var browser = new Browser(address =>
        {
            browserWork = Task.Run(async () =>
            {
                using var local = new HttpClient(new HttpClientHandler { UseProxy = false });
                var redirect = GoogleOAuthProtocol.Query(address)["redirect_uri"];
                using var rejected = await local.GetAsync(redirect + "?code=bad&state=wrong");
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                using var accepted = await local.GetAsync(redirect + "?code=local-code&state=expected");
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                Assert.DoesNotContain("local-code", await accepted.Content.ReadAsStringAsync());
            });
            return Task.CompletedTask;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var callback = await new GoogleLoopbackBroker(browser).AuthorizeAsync(uri => GoogleOAuthProtocol.AuthorizationUrl(ClientId, uri, "expected", "verifier"), "expected", timeout.Token);
        Assert.Equal("local-code", GoogleOAuthProtocol.Query(callback)["code"]); await browserWork!;
    }
    [Fact]
    public async Task LoopbackCancellationClosesItsListener()
    {
        Uri? redirect = null; var browser = new Browser(address => { redirect = new(GoogleOAuthProtocol.Query(address)["redirect_uri"]); return Task.CompletedTask; });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GoogleLoopbackBroker(browser).AuthorizeAsync(uri => GoogleOAuthProtocol.AuthorizationUrl(ClientId, uri, "expected", "verifier"), "expected", timeout.Token));
        using var replacement = new System.Net.Sockets.TcpListener(IPAddress.Loopback, redirect!.Port) { ExclusiveAddressUse = true };
        replacement.Start(); // Rebinding proves the canceled listener released its port.
    }
    [Fact]
    public async Task QueuedAuthorizationCannotMoveToANewTodoAccount()
    {
        await client.LoginAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.BeforeReturn = async () => { entered.SetResult(); await release.Task; };
        var first = ConnectAsync(); await entered.Task; var queued = ConnectAsync();
        await client.Session.SignOutAsync(); client.Account = client.Auth("second-token", "second@example.invalid"); await client.LoginAsync();
        release.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => first); await Assert.ThrowsAsync<GoogleAuthorizationException>(() => queued);
        Assert.Empty(vault.Values); Assert.Empty(handler.Requests);
    }
    private sealed class Broker : IGoogleAuthorizationBroker
    {
        public bool IsSupported => true;
        public bool WrongState { get; set; }
        public Uri? Authorization { get; private set; }
        public Func<Task>? BeforeReturn { get; set; }
        public async Task<Uri> AuthorizeAsync(Func<Uri, Uri> authorizationUrl, string state, CancellationToken cancellationToken)
        {
            var redirect = new Uri("http://127.0.0.1:54321/oauth2/callback"); Authorization = authorizationUrl(redirect);
            if (BeforeReturn is not null) await BeforeReturn();
            return new(redirect.AbsoluteUri + "?code=google-code&state=" + (WrongState ? "wrong" : state));
        }
    }
    private sealed class Vault : IGoogleTokenVault
    {
        public Dictionary<string, string> Values { get; } = new();
        public HashSet<string> Blocked { get; } = new();
        public bool FailWrite { get; set; }
        public bool FailBlock { get; set; }
        public Task<string?> ReadAsync(string scope) => Task.FromResult(Blocked.Contains(scope) ? null : Values.GetValueOrDefault(scope));
        public Task WriteAsync(string scope, string json)
        {
            Blocked.Add(scope); if (FailWrite) throw new IOException("Fake secure write failure");
            Values[scope] = json; Blocked.Remove(scope); return Task.CompletedTask;
        }
        public void Block(string scope) { if (FailBlock) throw new IOException("Fake marker failure"); Blocked.Add(scope); Values.Remove(scope); }
    }
    private sealed class Browser(Func<Uri, Task> open) : IGoogleSystemBrowser { public Task OpenAsync(Uri address) => open(address); }
}
