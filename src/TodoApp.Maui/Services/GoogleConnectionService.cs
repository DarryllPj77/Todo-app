using System.Text.Json;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed class GoogleConnectionService : IDisposable
{
    private readonly ClientSession session;
    private readonly IGoogleTokenVault vault;
    private readonly IGoogleAuthorizationBroker broker;
    private readonly GoogleOAuthProtocol protocol;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> blocked = new(StringComparer.Ordinal);
    private CancellationTokenSource? active;
    private int observedVersion;
    public bool IsSupported => broker.IsSupported;
    public GoogleConnectionService(ClientSession session, IGoogleTokenVault vault, IGoogleAuthorizationBroker broker,
        GoogleOAuthProtocol protocol, TimeProvider? clock = null)
    {
        this.session = session; this.vault = vault; this.broker = broker; this.protocol = protocol; this.clock = clock ?? TimeProvider.System;
        observedVersion = session.Version; session.Changed += SessionChanged;
    }
    private sealed record Context(string Scope, int Version);
    private Context Capture()
    {
        if (session.Current is not { } saved || !session.IsVerified || saved.ExpiresAt <= clock.GetUtcNow())
            throw new GoogleAuthorizationException("Sign in and verify the Todo account through Account & server first.");
        return new(SqliteSyncStore.Partition(saved.Origin, saved.User.Id), session.Version);
    }
    private void Ensure(Context context)
    {
        var current = Capture();
        if (current != context) throw new GoogleAuthorizationException("The Todo account changed. The old Google result was discarded.");
    }
    private async Task<T> RunAsync<T>(Func<Context, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var context = Capture();
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            active = linked; Ensure(context);
            var result = await action(context, linked.Token); Ensure(context); linked.Token.ThrowIfCancellationRequested(); return result;
        }
        finally { active = null; gate.Release(); }
    }
    private async Task<GoogleGrant?> LoadAsync(Context context)
    {
        if (blocked.Contains(context.Scope)) return null;
        var json = await vault.ReadAsync(context.Scope); Ensure(context);
        if (json is null) return null;
        try
        {
            var grant = JsonSerializer.Deserialize<GoogleGrant>(json) ?? throw new JsonException();
            GoogleOAuthProtocol.ValidateClient(grant.ClientId, grant.ClientSecret);
            if (grant.Scope != context.Scope || string.IsNullOrWhiteSpace(grant.Subject) || string.IsNullOrWhiteSpace(grant.Email)
                || string.IsNullOrWhiteSpace(grant.AccessToken) || string.IsNullOrWhiteSpace(grant.RefreshToken) || !GoogleOAuthProtocol.HasScopes(grant.GrantedScopes)
                || grant.AccessToken.Any(char.IsControl) || grant.RefreshToken.Any(char.IsControl) || grant.ExpiresAt.Offset != TimeSpan.Zero)
                throw new JsonException();
            return grant;
        }
        catch (Exception error) when (error is JsonException or GoogleAuthorizationException or ArgumentException)
        { Block(context.Scope); throw new GoogleAuthorizationException("The protected Google connection was invalid and blocked. Connect again."); }
    }
    private void Block(string scope)
    {
        blocked.Add(scope);
        try { vault.Block(scope); }
        catch (Exception) { throw new GoogleAuthorizationException("Google credentials are blocked in this process, but removal could not be persisted. Remove access in your Google Account and retry local disconnect."); }
    }
    private async Task SaveAsync(Context context, GoogleGrant grant, CancellationToken ct)
    {
        Ensure(context); ct.ThrowIfCancellationRequested(); blocked.Add(context.Scope);
        try
        {
            await vault.WriteAsync(context.Scope, JsonSerializer.Serialize(grant)); Ensure(context); ct.ThrowIfCancellationRequested();
            blocked.Remove(context.Scope);
        }
        catch
        {
            try { vault.Block(context.Scope); } catch (Exception) { /* In-process block remains, no old grant can be used here. */ }
            throw;
        }
    }
    public Task<GoogleConnectionInfo> ConnectAsync(string clientId, string secret, CancellationToken ct) => RunAsync(async (context, token) =>
    {
        if (!broker.IsSupported) throw new GoogleAuthorizationException("Google authorization is available on Windows desktop only in this increment.");
        clientId = clientId.Trim(); secret = secret.Trim(); GoogleOAuthProtocol.ValidateClient(clientId, secret);
        if (await LoadAsync(context) is not null) throw new GoogleAuthorizationException("A Google connection is already saved for this Todo account. Check it or disconnect before connecting another account/client.");
        var state = GoogleOAuthProtocol.RandomValue(); var verifier = GoogleOAuthProtocol.RandomValue(); Uri? redirect = null;
        var callback = await broker.AuthorizeAsync(uri => { redirect = uri; return GoogleOAuthProtocol.AuthorizationUrl(clientId, uri, state, verifier); }, state, token);
        Ensure(context); token.ThrowIfCancellationRequested();
        if (redirect is null) throw new GoogleAuthorizationException("The browser callback was not initialized.");
        var code = GoogleOAuthProtocol.CallbackCode(callback, redirect, state);
        var grant = await protocol.ExchangeAsync(context.Scope, clientId, secret, code, verifier, redirect, token); Ensure(context);
        grant = await protocol.IdentifyAsync(grant, token); Ensure(context);
        await SaveAsync(context, grant, token); return Info(grant);
    }, ct);
    public Task<GoogleConnectionInfo?> CheckAsync(CancellationToken ct) => RunAsync<GoogleConnectionInfo?>(async (context, token) =>
    {
        if (!broker.IsSupported) throw new GoogleAuthorizationException("Use Windows desktop to check or refresh this Google connection.");
        var grant = await LoadAsync(context); if (grant is null) return null;
        try
        {
            if (grant.ExpiresAt <= clock.GetUtcNow().AddMinutes(1)) { grant = await protocol.RefreshAsync(grant, token); Ensure(context); }
            grant = await protocol.IdentifyAsync(grant, token); Ensure(context);
            await SaveAsync(context, grant, token); return Info(grant);
        }
        catch (GoogleAuthorizationException error) when (error.InvalidGrant) { Block(context.Scope); throw; }
    }, ct);
    public Task<bool> ForgetAsync(CancellationToken ct) => RunAsync((context, _) => { Block(context.Scope); return Task.FromResult(true); }, ct);
    // The token stays inside services. Provider work shares the consent/disconnect gate.
    internal Task<T> UseGrantAsync<T>(string scope, string? subject, string? clientId,
        Func<GoogleGrant, Action, CancellationToken, Task<T>> action, CancellationToken ct) => RunAsync(async (context, token) =>
    {
        if (!broker.IsSupported || context.Scope != scope) throw new GoogleAuthorizationException("Choose the active Todo account on Windows desktop.");
        var grant = await LoadAsync(context) ?? throw new GoogleAuthorizationException("Connect Google for this Todo account first.");
        try
        {
            if (grant.ExpiresAt <= clock.GetUtcNow().AddMinutes(1)) { grant = await protocol.RefreshAsync(grant, token); Ensure(context); }
            grant = await protocol.IdentifyAsync(grant, token); Ensure(context);
            if (subject is not null && grant.Subject != subject || clientId is not null && grant.ClientId != clientId)
                throw new GoogleAuthorizationException("This link belongs to a different Google account/client. Reconnect the original identity before using it.");
            await SaveAsync(context, grant, token);
            void Guard() { Ensure(context); token.ThrowIfCancellationRequested(); }
            Guard(); var result = await action(grant, Guard, token); Guard(); return result;
        }
        catch (GoogleAuthorizationException error) when (error.InvalidGrant) { Block(context.Scope); throw; }
    }, ct);
    public Task<bool> RevokeAsync(CancellationToken ct) => RunAsync(async (context, token) =>
    {
        if (!broker.IsSupported) throw new GoogleAuthorizationException("Use Windows desktop to revoke this Google connection, or remove access in your Google Account.");
        var grant = await LoadAsync(context) ?? throw new GoogleAuthorizationException("No usable local Google credentials remain. Remove access from your Google Account if needed.");
        await protocol.RevokeAsync(grant, token);
        // Revocation may already have succeeded if the Todo account changed during HTTP.
        Block(context.Scope); Ensure(context); return true;
    }, ct);
    private static GoogleConnectionInfo Info(GoogleGrant grant) => new(grant.Email, grant.ClientId, grant.ExpiresAt);
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return;
        observedVersion = session.Version;
        try { active?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public void Dispose() { session.Changed -= SessionChanged; active?.Cancel(); }
}
