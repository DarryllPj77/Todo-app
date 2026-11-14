namespace TodoApp.Maui.Models;

public sealed class GoogleAuthorizationException(string message, bool invalidGrant = false) : Exception(message)
{
    public bool InvalidGrant { get; } = invalidGrant;
}

public sealed record GoogleConnectionInfo(string Email, string ClientId, DateTimeOffset ExpiresAt);

// Never expose this object through binding or generated record ToString output.
public sealed class GoogleGrant
{
    public string Scope { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Email { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string GrantedScopes { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public override string ToString() => "Protected Google authorization";
}
