using System.Security.Cryptography;
using System.Text;

namespace TodoApp.Maui.Services;

public sealed class GoogleTokenVault : IGoogleTokenVault
{
    private static string Key(string scope) => "todoapp.google.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    public Task<string?> ReadAsync(string scope) => Preferences.Default.Get(Key(scope) + ".blocked", false)
        ? Task.FromResult<string?>(null) : SecureStorage.Default.GetAsync(Key(scope));
    public async Task WriteAsync(string scope, string json)
    {
        var key = Key(scope); Preferences.Default.Set(key + ".blocked", true);
        await SecureStorage.Default.SetAsync(key, json);
        Preferences.Default.Set(key + ".blocked", false);
    }
    public void Block(string scope)
    {
        var key = Key(scope); Preferences.Default.Set(key + ".blocked", true);
        try { SecureStorage.Default.Remove(key); } catch (Exception) { /* Durable block prevents restoration if secure deletion fails. */ }
    }
}

public sealed class GoogleSystemBrowser : IGoogleSystemBrowser
{
    public async Task OpenAsync(Uri address)
    {
        if (!await Launcher.Default.OpenAsync(address)) throw new InvalidOperationException("System browser is unavailable.");
    }
}

public sealed class UnsupportedGoogleBroker : IGoogleAuthorizationBroker
{
    public bool IsSupported => false;
    public Task<Uri> AuthorizeAsync(Func<Uri, Uri> authorizationUrl, string state, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Use Windows desktop for this Google authorization increment.");
}
