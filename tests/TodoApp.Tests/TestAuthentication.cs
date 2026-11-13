using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoApp.Api.Services;

namespace TodoApp.Tests;

internal static class TestAuthentication
{
    public static readonly Guid UserId = Guid.Parse("a02d0729-d9d0-41bd-a74a-c42e818b9afd");
    public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static void Register(IServiceCollection services) => services.AddAuthentication("Test")
        .AddScheme<AuthenticationSchemeOptions, Handler>("Test", _ => { });

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(new[] { new Claim("sub", UserId.ToString()) }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid UserId => TestAuthentication.UserId;
}
