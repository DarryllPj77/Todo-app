using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Api.Configuration;
using TodoApp.Api.Contracts;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Services;

public sealed class JwtTokenService(IOptions<JwtSettings> options)
{
    public AuthResponse Issue(UserAccount user)
    {
        var settings = options.Value;
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            now.UtcDateTime, expires.UtcDateTime, credentials);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires,
            new UserResponse(user.Id, user.Email));
    }
}
