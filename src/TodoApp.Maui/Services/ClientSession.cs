using System.Text.Json;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed class ClientSession(IClientSettings settings, ISecureTokenStore storage, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim storageGate = new(1, 1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private bool restored;
    private int version;
    public SavedSession? Current { get; private set; }
    public bool IsVerified { get; private set; }
    public int Version => Volatile.Read(ref version);
    public event Action? Changed;

    public Uri Endpoint => ParseEndpoint(settings.ApiBaseUrl);
    public string ConfiguredEndpoint => settings.ApiBaseUrl;

    public static Uri ParseEndpoint(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ApiClientException("Enter an HTTPS API address with no credentials, path, query, or fragment.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public async Task RestoreAsync()
    {
        if (restored) return;
        restored = true;
        if (Current is not null) return;
        if (settings.RestorationBlocked) return;
        var expectedVersion = Version;
        await storageGate.WaitAsync();
        try
        {
            var json = await storage.GetAsync();
            if (Version != expectedVersion || settings.RestorationBlocked) return;
            if (string.IsNullOrWhiteSpace(json)) return;
            var saved = JsonSerializer.Deserialize<SavedSession>(json);
            if (saved is null || saved.Origin != Endpoint.AbsoluteUri || saved.ExpiresAt <= clock.GetUtcNow() ||
                string.IsNullOrWhiteSpace(saved.AccessToken) || saved.User is null || saved.User.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(saved.User.Email))
            {
                settings.RestorationBlocked = true;
                storage.Clear();
                return;
            }

            Current = saved;
            IsVerified = false; // /me must confirm the account before workspace use.
            Interlocked.Increment(ref version);
            Changed?.Invoke();
        }
        catch (Exception)
        {
            TryBlockRestoration();
            throw new ApiClientException("Couldn't read the saved session. Use Sign out / clear session, then sign in again.");
        }
        finally { storageGate.Release(); }
    }

    public async Task<int> SignOutAsync()
    {
        Current = null;
        IsVerified = false;
        var signedOutVersion = Interlocked.Increment(ref version);
        Changed?.Invoke();
        // Write the non-secret block before deleting the protected secret.
        var blocked = TryBlockRestoration();
        await storageGate.WaitAsync();
        try
        {
            // A newer sign-in/logout owns storage now; don't clear its token.
            if (Version == signedOutVersion) storage.Clear();
        }
        catch (Exception)
        {
            throw new ApiClientException(blocked
                ? "Signed out locally, but secure storage couldn't be cleared. Retry clear session before signing in again."
                : "Signed out locally, but the device couldn't clear or block the saved session. Keep the app open and retry clear session.");
        }
        finally { storageGate.Release(); }
        return signedOutVersion;
    }

    public async Task ChangeEndpointAsync(string input)
    {
        var endpoint = ParseEndpoint(input);
        var signedOutVersion = await SignOutAsync();
        EnsureVersion(signedOutVersion);
        settings.ApiBaseUrl = endpoint.AbsoluteUri;
        Interlocked.Increment(ref version);
        Changed?.Invoke();
    }

    public async Task SaveAsync(ApiAuthResponse response, Uri origin, int expectedVersion)
    {
        if (response.User is null || response.User.Id == Guid.Empty || string.IsNullOrWhiteSpace(response.User.Email) || string.IsNullOrWhiteSpace(response.AccessToken) ||
            !string.Equals(response.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || response.ExpiresAt <= clock.GetUtcNow())
            throw new ApiClientException("The API returned an invalid session.");
        await storageGate.WaitAsync();
        try
        {
            EnsureVersion(expectedVersion);
            if (origin.AbsoluteUri != Endpoint.AbsoluteUri) throw new ApiClientException("The API address changed. Sign in again.");
            var saved = new SavedSession(origin.AbsoluteUri, response.AccessToken, response.ExpiresAt, response.User);
            await storage.SetAsync(JsonSerializer.Serialize(saved));
            if (Version != expectedVersion)
            {
                storage.Clear();
                throw new ApiClientException("The session changed. Please sign in again.");
            }

            settings.RestorationBlocked = false;
            Current = saved;
            IsVerified = true;
            Interlocked.Increment(ref version);
            Changed?.Invoke();
        }
        catch (ApiClientException) { throw; }
        catch (Exception)
        {
            Current = null;
            IsVerified = false;
            Interlocked.Increment(ref version);
            Changed?.Invoke();
            TryBlockRestoration();
            try { storage.Clear(); } catch (Exception) { /* Keep restoration blocked; caller must retry clearing. */ }
            throw new ApiClientException("Couldn't securely save the session. No plaintext copy was made; clear session and retry.");
        }
        finally { storageGate.Release(); }
    }

    public async Task<SavedSession> RequireAsync(bool allowUnverified = false)
    {
        var saved = Current ?? throw new ApiClientException("Sign in to use the server workspace.");
        if (saved.ExpiresAt <= clock.GetUtcNow() || saved.Origin != Endpoint.AbsoluteUri)
        {
            await SignOutAsync();
            throw new ApiClientException("Your session expired or the API changed. Sign in again.");
        }
        if (!allowUnverified && !IsVerified) throw new ApiClientException("Verify the saved session before using server tasks.");
        return saved;
    }

    public void Verify(ApiUser user, int expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Current?.User.Id != user.Id) throw new ApiClientException("The saved account did not match the API response. Clear session and sign in again.");
        IsVerified = true;
        Changed?.Invoke();
    }

    public void EnsureVersion(int expected)
    {
        if (version != expected) throw new ApiClientException("The account or API address changed; the old response was discarded.");
    }

    private bool TryBlockRestoration()
    {
        try { settings.RestorationBlocked = true; return true; }
        catch (Exception) { return false; }
    }
}
