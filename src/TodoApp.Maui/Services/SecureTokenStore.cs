namespace TodoApp.Maui.Services;

// Phase 9 will use this after API login. Never store a password or fall back to Preferences/files.
public sealed class SecureTokenStore
{
    private const string AccessTokenKey = "todoapp.access-token";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(AccessTokenKey);
    public Task SetAsync(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return SecureStorage.Default.SetAsync(AccessTokenKey, accessToken);
    }
    public void Clear() => SecureStorage.Default.Remove(AccessTokenKey);
}
