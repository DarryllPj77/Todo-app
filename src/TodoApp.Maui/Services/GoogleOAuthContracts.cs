namespace TodoApp.Maui.Services;

public interface IGoogleTokenVault
{
    Task<string?> ReadAsync(string scope);
    Task WriteAsync(string scope, string json);
    void Block(string scope);
}

public interface IGoogleAuthorizationBroker
{
    bool IsSupported { get; }
    Task<Uri> AuthorizeAsync(Func<Uri, Uri> authorizationUrl, string state, CancellationToken cancellationToken);
}

public interface IGoogleSystemBrowser
{
    Task OpenAsync(Uri address);
}
