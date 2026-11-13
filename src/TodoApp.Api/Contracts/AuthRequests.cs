using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TodoApp.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}

public sealed record UserResponse(Guid Id, string Email);
public sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserResponse User);
