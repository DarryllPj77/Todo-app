using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TodoApp.Api.Contracts;
using TodoApp.Infrastructure.Persistence.Server;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests : IAsyncLifetime
{
    private readonly string databaseName = "todoapp_tests_" + Guid.NewGuid().ToString("N");
    private string adminConnection = null!;
    private string connectionString = null!;
    private WebApplicationFactory<Program> host = null!;
    private HttpClient client = null!;
    private const string Password = "LearningTodo!2026";

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("TODOAPP_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", admin);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName, Pooling = false }.ToString();
        await using var database = Context();
        await database.Database.MigrateAsync();
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TodoDatabase"] = connectionString,
                    ["Jwt:SigningKey"] = TestAuthentication.SigningKey
                })));
        client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (host is not null) await host.DisposeAsync();
        if (connectionString is null) return;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\";", admin);
        await command.ExecuteNonQueryAsync();
    }

    private TodoDbContext Context() => new(new DbContextOptionsBuilder<TodoDbContext>().UseNpgsql(connectionString).Options);
    private void As(AuthResponse user) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

    private async Task<AuthResponse> Register(string email, string password = Password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    [PostgresFact]
    public async Task RegistrationLoginAndMe_UseHashedPasswordsAndShortLivedBearerTokens()
    {
        var registered = await Register("Learner@example.invalid");
        Assert.Equal("Bearer", registered.TokenType);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(registered.AccessToken);
        Assert.Equal(registered.User.Id.ToString(), jwt.Subject);
        Assert.Equal(registered.ExpiresAt.UtcDateTime, jwt.ValidTo);
        Assert.InRange(registered.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        await using var database = Context();
        var stored = await database.Users.SingleAsync();
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<UserAccount>().VerifyHashedPassword(stored, stored.PasswordHash, Password));

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "learner@EXAMPLE.invalid", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loggedIn = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(registered.User, loggedIn.User);
        Assert.NotEqual(registered.AccessToken, loggedIn.AccessToken);
        As(loggedIn);
        Assert.Equal(registered.User, await client.GetFromJsonAsync<UserResponse>("/api/auth/me"));
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));
    }

    [PostgresFact]
    public async Task EveryTaskOperation_EnforcesOwnershipAndCategoriesArePerUser()
    {
        var alice = await Register("alice@example.invalid");
        var bob = await Register("bob@example.invalid");
        As(alice);
        using var aliceCreate = await client.PostAsJsonAsync("/api/tasks", new { title = "Alice only", categoryName = "Work" });
        Assert.Equal(HttpStatusCode.Created, aliceCreate.StatusCode);
        using var aliceJson = JsonDocument.Parse(await aliceCreate.Content.ReadAsStringAsync());
        var aliceId = aliceJson.RootElement.GetProperty("id").GetGuid();
        var aliceCategory = aliceJson.RootElement.GetProperty("categoryId").GetGuid();

        As(bob);
        using var bobCreate = await client.PostAsJsonAsync("/api/tasks", new { title = "Bob only", categoryName = "Work" });
        using var bobJson = JsonDocument.Parse(await bobCreate.Content.ReadAsStringAsync());
        Assert.NotEqual(aliceCategory, bobJson.RootElement.GetProperty("categoryId").GetGuid());
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/tasks"));
        Assert.Equal("Bob only", Assert.Single(list.RootElement.EnumerateArray()).GetProperty("title").GetString());
        using var get = await client.GetAsync($"/api/tasks/{aliceId}");
        using var put = await client.PutAsJsonAsync($"/api/tasks/{aliceId}", new { title = "Stolen", categoryName = "Attack" });
        using var delete = await client.DeleteAsync($"/api/tasks/{aliceId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var spoof = await client.PostAsJsonAsync("/api/tasks", new { title = "Attack", ownerId = alice.User.Id });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        As(alice);
        using var unchanged = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{aliceId}"));
        Assert.Equal("Alice only", unchanged.RootElement.GetProperty("title").GetString());
        await using var database = Context();
        Assert.Equal(2, await database.Tasks.CountAsync());
        Assert.Equal(2, await database.Categories.CountAsync());
    }

    [PostgresFact]
    public async Task AnonymousTaskRoutes_AndInvalidJwtTokensAreRejected()
    {
        foreach (var operation in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            var path = operation == HttpMethod.Get || operation == HttpMethod.Post ? "/api/tasks" : $"/api/tasks/{Guid.NewGuid()}";
            using var request = new HttpRequestMessage(operation, path);
            if (operation == HttpMethod.Post || operation == HttpMethod.Put) request.Content = JsonContent.Create(new { title = "Unauthorized" });
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var registered = await Register("jwt@example.invalid");
        var wrongKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var invalid = new[]
        {
            "not-a-jwt", Token(registered.User.Id, key: wrongKey),
            Token(registered.User.Id, issuer: "WrongIssuer"), Token(registered.User.Id, audience: "WrongAudience"),
            Token(registered.User.Id, expired: true), Token(registered.User.Id, future: true),
            Token(registered.User.Id, unsigned: true), Token(Guid.NewGuid())
        };
        foreach (var token in invalid)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.GetAsync("/api/tasks");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [PostgresFact]
    public async Task LoginFailuresAreGeneric_AndDoNotPreventAValidLogin()
    {
        await Register("login@example.invalid");
        using var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "login@example.invalid", password = "WrongPassword!2026" });
        using var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.invalid", password = "WrongPassword!2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrong.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            (await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        using var valid = await client.PostAsJsonAsync("/api/auth/login", new { email = "login@example.invalid", password = Password });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [PostgresFact]
    public async Task Registration_RejectsDuplicateEmailsInvalidFieldsAndPrivilegedInput()
    {
        await Register("unique@example.invalid");
        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email = "UNIQUE@example.invalid", password = Password });
        using var email = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = Password });
        using var password = await client.PostAsJsonAsync("/api/auth/register", new { email = "short@example.invalid", password = "short" });
        using var privileged = await client.PostAsJsonAsync("/api/auth/register", new { email = "role@example.invalid", password = Password, role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, email.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, password.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, privileged.StatusCode);
        await using var database = Context();
        Assert.Equal(1, await database.Users.CountAsync());
    }

    [PostgresFact]
    public async Task AuthRateLimit_RejectsTheEleventhRequestInTheWindow()
    {
        for (var number = 1; number <= 11; number++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "rate@example.invalid", password = Password });
            Assert.Equal(number <= 10 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [PostgresFact]
    public async Task OwnershipMigration_PreservesLegacyRowsWithoutAssigningThemToNewUsers()
    {
        var legacyId = Guid.NewGuid();
        await using (var database = Context())
        {
            await database.GetService<IMigrator>().MigrateAsync("InitialCreate");
            var now = DateTimeOffset.UtcNow;
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Tasks" ("Id", "Title", "IsCompleted", "Priority", "CreatedAt", "UpdatedAt")
                VALUES ({legacyId}, 'Legacy anonymous task', false, 1, {now}, {now});
                """);
            await database.Database.MigrateAsync();
            Assert.Equal(legacyId, (await database.Tasks.SingleAsync()).Id);
            Assert.Null(await database.Tasks.Select(task => EF.Property<Guid?>(task, "OwnerId")).SingleAsync());
            Assert.False(database.Database.HasPendingModelChanges());
        }

        As(await Register("new-user@example.invalid"));
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/tasks"));
        Assert.Empty(list.RootElement.EnumerateArray());
        using var hidden = await client.GetAsync($"/api/tasks/{legacyId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [PostgresFact]
    public async Task DeletedAccount_InvalidatesItsAccessToken()
    {
        var account = await Register("deleted@example.invalid");
        await using (var database = Context()) await database.Users.ExecuteDeleteAsync();
        As(account);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task PasswordWhitespace_IsNotSilentlyTrimmed()
    {
        const string password = "  Password with spaces 2026  ";
        await Register("spaces@example.invalid", password);
        using var trimmed = await client.PostAsJsonAsync("/api/auth/login", new { email = "spaces@example.invalid", password = password.Trim() });
        using var exact = await client.PostAsJsonAsync("/api/auth/login", new { email = "spaces@example.invalid", password });
        Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [PostgresFact]
    public async Task MauiApiClient_CompletesAuthenticatedCrudAndRestoresAnOriginBoundSession()
    {
        var settings = new MemoryClientSettings();
        var protectedStorage = new MemoryTokenStore();
        var session = new ClientSession(settings, protectedStorage);
        var api = new TodoApiClient(client, session);
        await api.SignInAsync("maui-client@example.invalid", Password, register: true);
        Assert.True(session.IsVerified);
        var created = await api.CreateAsync(new ApiTaskWrite("Client task", "Client details",
            new DateOnly(2026, 11, 20), TodoApp.Domain.Enums.TaskPriority.High, "Client category"));
        Assert.Equal(created.Id, Assert.Single(await api.GetTasksAsync()).Id);
        Assert.Equal("Client details", created.Description);
        Assert.Equal(TodoApp.Domain.Enums.TaskPriority.High, created.Priority);

        var restartedSession = new ClientSession(settings, protectedStorage);
        await restartedSession.RestoreAsync();
        Assert.False(restartedSession.IsVerified);
        var restartedApi = new TodoApiClient(client, restartedSession);
        await restartedApi.VerifySessionAsync();
        Assert.True(restartedSession.IsVerified);
        var updated = await restartedApi.UpdateAsync(created.Id, new ApiTaskWrite("Edited by client", IsCompleted: true));
        Assert.True(updated.IsCompleted);
        Assert.Null(updated.DueDate);
        Assert.Null(updated.CategoryId);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        await restartedApi.DeleteAsync(created.Id);
        Assert.Empty(await restartedApi.GetTasksAsync());
        await restartedSession.SignOutAsync();
        Assert.Null(protectedStorage.Value);
        var signedOutRestart = new ClientSession(settings, protectedStorage);
        await signedOutRestart.RestoreAsync();
        Assert.Null(signedOutRestart.Current);
    }

    private static string Token(Guid subject, string? key = null, string issuer = "TodoApp.Development",
        string audience = "TodoApp.Client", bool expired = false, bool future = false, bool unsigned = false)
    {
        var now = DateTime.UtcNow;
        var signing = unsigned ? null : new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(key ?? TestAuthentication.SigningKey)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
            new[] { new Claim("sub", subject.ToString()) },
            now.AddMinutes(future ? 5 : -10), now.AddMinutes(expired ? -1 : 15), signing));
    }
}
