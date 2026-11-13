using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authentication, TodoDbContext database, ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.RegisterAsync(request, cancellationToken);
        return result is null ? Problem(statusCode: 409, title: "Registration could not be completed with those details.")
            : CreatedAtAction(nameof(Me), result);
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(request, cancellationToken);
        return result is null ? Problem(statusCode: 401, title: "Invalid email or password.") : Ok(result);
    }

    [Authorize, HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == currentUser.UserId, cancellationToken);
        return Ok(new UserResponse(user.Id, user.Email));
    }
}
