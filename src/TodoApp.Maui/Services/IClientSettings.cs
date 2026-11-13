namespace TodoApp.Maui.Services;

public interface IClientSettings
{
    string ApiBaseUrl { get; set; }
    bool RestorationBlocked { get; set; }
}

public interface ISecureTokenStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
    void Clear();
}
