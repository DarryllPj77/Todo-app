using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TodoApp.Api.Contracts;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Services;

public sealed class AuthService(TodoDbContext database, IPasswordHasher<UserAccount> hasher, JwtTokenService tokens)
{
    // Unknown-account logins still perform the expensive password verification.
    private static readonly UserAccount DummyUser = new("unknown@example.invalid");
    private static readonly string DummyHash = new PasswordHasher<UserAccount>(Microsoft.Extensions.Options.Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 })).HashPassword(DummyUser,
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new UserAccount(request.Email);
        user.SetPasswordHash(hasher.HashPassword(user, request.Password));
        database.Users.Add(user);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_NormalizedEmail" })
        {
            return null;
        }

        return tokens.Issue(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var key = request.Email.Trim().ToUpperInvariant();
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == key, cancellationToken);
        var result = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, request.Password));
            await database.SaveChangesAsync(cancellationToken);
        }

        return tokens.Issue(user);
    }
}
