using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Api.Services;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Configuration;

public static class AuthenticationConfiguration
{
    public static IServiceCollection AddTaskAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtSettings>().BindConfiguration("Jwt")
            .Validate(settings => settings.IsValid(), "Configure Jwt:SigningKey as at least 32 random bytes encoded in Base64; issuer/audience and a 1–60 minute lifetime are required.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, configured) =>
            {
                var settings = configured.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = settings.Issuer,
                    ValidateAudience = true, ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
                    RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }, ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst("sub")?.Value;
                        if (!Guid.TryParse(subject, out var userId) ||
                            !await context.HttpContext.RequestServices.GetRequiredService<TodoDbContext>()
                                .Users.AsNoTracking().AnyAsync(user => user.Id == userId, context.HttpContext.RequestAborted))
                            context.Fail("Invalid account.");
                    }
                };
            });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<AuthService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
        });
        return services;
    }
}
