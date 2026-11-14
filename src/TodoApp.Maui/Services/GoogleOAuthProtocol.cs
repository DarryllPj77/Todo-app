using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed class GoogleOAuthProtocol(HttpClient http, TimeProvider? clock = null)
{
    public const string CalendarListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    public const string EventsScope = "https://www.googleapis.com/auth/calendar.events.owned";
    public const string Scopes = "openid email " + CalendarListScope + " " + EventsScope;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static void ValidateClient(string clientId, string secret)
    {
        if (string.IsNullOrEmpty(clientId) || clientId.Length is < 10 or > 256 || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
            || clientId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-'))
            throw new GoogleAuthorizationException("Enter the Desktop app OAuth client ID from Google Cloud.");
        if (secret is null || secret.Length > 512 || secret.Any(char.IsControl)) throw new GoogleAuthorizationException("Review the optional Desktop client secret.");
    }
    public static Uri AuthorizationUrl(string clientId, Uri redirect, string state, string verifier)
    {
        ValidateRedirect(redirect);
        var values = new Dictionary<string, string> { ["client_id"] = clientId, ["redirect_uri"] = redirect.AbsoluteUri,
            ["response_type"] = "code", ["scope"] = Scopes, ["state"] = state, ["code_challenge"] = Challenge(verifier),
            ["code_challenge_method"] = "S256", ["access_type"] = "offline", ["prompt"] = "consent select_account" };
        return new("https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }
    public static void ValidateRedirect(Uri redirect)
    {
        if (redirect.Scheme != "http" || redirect.Host != "127.0.0.1" || redirect.Port <= 0 || redirect.AbsolutePath != "/oauth2/callback"
            || redirect.Query.Length > 0 || redirect.Fragment.Length > 0 || redirect.UserInfo.Length > 0)
            throw new GoogleAuthorizationException("Google authorization requires the desktop loopback callback.");
    }
    public static Dictionary<string, string> Query(Uri address)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            if (!result.TryAdd(key, value)) throw new GoogleAuthorizationException("Duplicate authorization response fields were rejected.");
        }
        return result;
    }
    public static string CallbackCode(Uri callback, Uri redirect, string expectedState)
    {
        if (callback.GetLeftPart(UriPartial.Path) != redirect.AbsoluteUri || callback.Fragment.Length != 0 || callback.UserInfo.Length != 0)
            throw new GoogleAuthorizationException("The authorization callback did not match this request.");
        var values = Query(callback);
        if (!values.TryGetValue("state", out var state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            throw new GoogleAuthorizationException("The Google authorization state did not match. Start Connect again.");
        if (values.ContainsKey("error")) throw new GoogleAuthorizationException("Google authorization was denied or canceled. No connection was saved.");
        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 4096 || code.Any(char.IsControl))
            throw new GoogleAuthorizationException("Google did not return a valid authorization code.");
        return code;
    }

    public Task<GoogleGrant> ExchangeAsync(string scope, string clientId, string secret, string code, string verifier, Uri redirect, CancellationToken ct) =>
        TokensAsync(scope, clientId, secret, new() { ["grant_type"] = "authorization_code", ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect.AbsoluteUri }, null, ct);
    public Task<GoogleGrant> RefreshAsync(GoogleGrant prior, CancellationToken ct) => TokensAsync(prior.Scope, prior.ClientId, prior.ClientSecret,
        new() { ["grant_type"] = "refresh_token", ["refresh_token"] = prior.RefreshToken }, prior, ct);
    private async Task<GoogleGrant> TokensAsync(string scope, string clientId, string secret, Dictionary<string, string> fields, GoogleGrant? prior, CancellationToken ct)
    {
        fields["client_id"] = clientId;
        if (secret.Length > 0) fields["client_secret"] = secret;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") { Content = new FormUrlEncodedContent(fields) };
        using var response = await http.SendAsync(request, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var json = document.RootElement;
        if (!response.IsSuccessStatusCode)
            throw new GoogleAuthorizationException("Google token authorization failed. Check client configuration or reconnect after revoked/expired consent.", Text(json, "error") == "invalid_grant");
        var access = Text(json, "access_token"); var refresh = Text(json, "refresh_token") ?? prior?.RefreshToken;
        var granted = Text(json, "scope") ?? prior?.GrantedScopes ?? Scopes;
        if (Text(json, "token_type")?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) != true || string.IsNullOrWhiteSpace(access)
            || access.Length > 16384 || access.Any(char.IsControl) || string.IsNullOrWhiteSpace(refresh) || refresh.Length > 16384 || refresh.Any(char.IsControl)
            || !json.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt32(out var seconds) || seconds is < 30 or > 86400 || !HasScopes(granted))
            throw new GoogleAuthorizationException("Google did not grant the required scopes, offline refresh token, or usable expiry. Reconnect with consent.");
        return new() { Scope = scope, ClientId = clientId, ClientSecret = secret, AccessToken = access, RefreshToken = refresh,
            GrantedScopes = granted, ExpiresAt = time.GetUtcNow().AddSeconds(seconds), Subject = prior?.Subject ?? "", Email = prior?.Email ?? "" };
    }
    public static bool HasScopes(string scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes)) return false;
        var values = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return values.Contains("openid") && (values.Contains("email") || values.Contains("https://www.googleapis.com/auth/userinfo.email"))
            && values.Contains(CalendarListScope) && values.Contains(EventsScope);
    }
    public async Task<GoogleGrant> IdentifyAsync(GoogleGrant grant, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", grant.AccessToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new GoogleAuthorizationException("Google could not verify this connection. Reconnect if access was revoked.", response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var json = document.RootElement; var subject = Text(json, "sub"); var email = Text(json, "email");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || subject.Any(char.IsControl) || string.IsNullOrWhiteSpace(email)
            || email.Length > 320 || email.Any(char.IsControl) || !json.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True
            || grant.Subject.Length > 0 && grant.Subject != subject)
            throw new GoogleAuthorizationException("The Google account identity was missing, unverified, or changed. Disconnect and reconnect.", true);
        return new() { Scope = grant.Scope, ClientId = grant.ClientId, ClientSecret = grant.ClientSecret, Subject = subject, Email = email,
            AccessToken = grant.AccessToken, RefreshToken = grant.RefreshToken, GrantedScopes = grant.GrantedScopes, ExpiresAt = grant.ExpiresAt };
    }
    public async Task RevokeAsync(GoogleGrant grant, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = grant.RefreshToken }) };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new GoogleAuthorizationException("Google revocation was not confirmed. Retry, or remove access in your Google Account. Local credentials were retained for retry.");
    }
    private static string? Text(JsonElement json, string name) => json.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
