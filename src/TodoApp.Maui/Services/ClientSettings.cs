namespace TodoApp.Maui.Services;

public sealed class ClientSettings : IClientSettings
{
    private static string DefaultUrl => DeviceInfo.Platform == DevicePlatform.Android
        ? "https://10.0.2.2:7114/" : "https://localhost:7114/";

    public string ApiBaseUrl
    {
        get => Preferences.Default.Get("todoapp.api-url", DefaultUrl);
        set => Preferences.Default.Set("todoapp.api-url", value);
    }

    // Non-secret logout state survives a secure-storage removal failure/restart.
    public bool RestorationBlocked
    {
        get => Preferences.Default.Get("todoapp.session-blocked", false);
        set => Preferences.Default.Set("todoapp.session-blocked", value);
    }
}
