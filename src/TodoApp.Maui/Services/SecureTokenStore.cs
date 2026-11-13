namespace TodoApp.Maui.Services;

// The JSON session includes its token, expiry, account, and API origin.
// Never store a password or fall back to Preferences/files.
public sealed class SecureTokenStore : ISecureTokenStore
{
    private const string AccessTokenKey = "todoapp.session.v1";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(AccessTokenKey);
    public Task SetAsync(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return SecureStorage.Default.SetAsync(AccessTokenKey, accessToken);
    }
    public void Clear() => SecureStorage.Default.Remove(AccessTokenKey);
}
