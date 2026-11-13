using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Tests;

public sealed class PostgresPersistenceTests : IAsyncLifetime
{
    private readonly string databaseName = "todoapp_tests_" + Guid.NewGuid().ToString("N");
    private string adminConnection = null!;
    private string connectionString = null!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false) }
    };

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("TODOAPP_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        // Identifiers are generated solely from a fixed prefix and Guid, never input.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", admin);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = databaseName,
            Pooling = false
        }.ToString();
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
        var testUser = new UserAccount("contract-tests@example.invalid");
        testUser.SetPasswordHash("NotUsedByTheExplicitTestAuthenticationHandler");
        context.Users.Add(testUser);
        context.Entry(testUser).Property(user => user.Id).CurrentValue = TestAuthentication.UserId;
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (connectionString is null) return;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\";", admin);
        await command.ExecuteNonQueryAsync();
    }

    private TodoDbContext OpenContext() => new(new DbContextOptionsBuilder<TodoDbContext>()
        .UseNpgsql(connectionString).Options);

    private WebApplicationFactory<Program> OpenHost() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TodoDatabase"] = connectionString,
                    ["Jwt:SigningKey"] = TestAuthentication.SigningKey
                }))
            .ConfigureServices(TestAuthentication.Register));

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [PostgresFact]
    public async Task InitialMigration_IsRepeatableAndMatchesCurrentModel()
    {
        await using var context = OpenContext();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [PostgresFact]
    public async Task RestCrud_PreservesAllFieldsAndDataAcrossNewHosts()
    {
        TaskResponse created;
        await using (var firstHost = OpenHost())
        {
            using var client = Client(firstHost);
            using var response = await client.PostAsJsonAsync("/api/tasks", new TaskWriteRequest
            {
                Title = " Learn EF ", Description = " Notes ", Priority = TaskPriority.High,
                CategoryName = "Learning", DueDate = new DateOnly(2026, 11, 20)
            }, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = (await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
            Assert.Equal($"/api/tasks/{created.Id}", response.Headers.Location!.AbsolutePath);
        }

        await using var secondHost = OpenHost();
        using var second = Client(secondHost);
        Assert.Equal(created, await second.GetFromJsonAsync<TaskResponse>($"/api/tasks/{created.Id}", JsonOptions));
        using var update = await second.PutAsJsonAsync($"/api/tasks/{created.Id}", new TaskWriteRequest
        {
            Title = "Updated", IsCompleted = true
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.IsCompleted);
        Assert.Null(updated.Description);
        Assert.Null(updated.DueDate);
        Assert.Null(updated.CategoryId);
        Assert.Equal(TaskPriority.Normal, updated.Priority);
        Assert.Equal(updated, await second.GetFromJsonAsync<TaskResponse>($"/api/tasks/{created.Id}", JsonOptions));
        using var deleted = await second.DeleteAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await second.GetAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [PostgresFact]
    public async Task ParallelCreates_UseOneNormalizedCategoryAndRetainEveryTask()
    {
        await using var host = OpenHost();
        using var client = Client(host);
        var writes = Enumerable.Range(1, 8).Select(async number =>
        {
            using var response = await client.PostAsJsonAsync("/api/tasks", new TaskWriteRequest
            {
                Title = $"Task {number}", CategoryName = number % 2 == 0 ? " work " : "Work"
            }, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
        });
        var created = await Task.WhenAll(writes);
        Assert.Single(created.Select(task => task.CategoryId).Distinct());
        await using var context = OpenContext();
        Assert.Equal(8, await context.Tasks.CountAsync());
        Assert.Equal(1, await context.Categories.CountAsync());
    }

    [PostgresFact]
    public async Task ForeignKeyDeletion_PreservesTaskAndClearsCategoryIdWithoutLoadingNavigation()
    {
        Guid id;
        await using (var context = OpenContext())
        {
            var service = new PostgresTaskService(context, new TestCurrentUser());
            id = (await service.CreateAsync(new TaskWriteRequest { Title = "Keep", CategoryName = "Work" }, default)).Id;
        }

        await using (var context = OpenContext())
        {
            var taskWithoutNavigation = await context.Tasks.AsNoTracking().SingleAsync();
            Assert.NotNull(taskWithoutNavigation.CategoryId);
            Assert.Null(taskWithoutNavigation.Category);
            await context.Categories.ExecuteDeleteAsync();
        }

        await using var reopened = OpenContext();
        var remaining = await reopened.Tasks.SingleAsync(task => task.Id == id);
        Assert.Equal("Keep", remaining.Title);
        Assert.Null(remaining.CategoryId);
    }

    [PostgresFact]
    public async Task ClearingCategoryWithoutLoadedNavigation_PersistsTheNullForeignKey()
    {
        Guid id;
        await using (var context = OpenContext())
        {
            id = (await new PostgresTaskService(context, new TestCurrentUser()).CreateAsync(new TaskWriteRequest
                { Title = "Keep", CategoryName = "Work" }, default)).Id;
        }

        await using (var context = OpenContext())
        {
            var task = await context.Tasks.SingleAsync();
            Assert.Null(task.Category);
            Assert.NotNull(task.CategoryId);
            task.SetCategory(null);
            await context.SaveChangesAsync();
        }

        await using var reopened = OpenContext();
        Assert.Null((await reopened.Tasks.SingleAsync(task => task.Id == id)).CategoryId);
        Assert.Single(await reopened.Categories.ToListAsync());
    }

    [PostgresFact]
    public async Task FailedTaskInsert_RollsBackCategoryAndSurfacesFailure()
    {
        await using (var context = OpenContext())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_test_task() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Test write failure'; END; $$;
                CREATE TRIGGER reject_test_task BEFORE INSERT ON "Tasks"
                FOR EACH ROW EXECUTE FUNCTION reject_test_task();
                """);
            var service = new PostgresTaskService(context, new TestCurrentUser());
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(new TaskWriteRequest
            {
                Title = "Not saved", CategoryName = "Not saved"
            }, default));
        }

        await using var reopened = OpenContext();
        Assert.Empty(await reopened.Tasks.ToListAsync());
        Assert.Empty(await reopened.Categories.ToListAsync());
    }

    [PostgresFact]
    public async Task RejectedHttpWriteAndMissingIds_DoNotMutateStoredData()
    {
        await using var host = OpenHost();
        using var client = Client(host);
        using var created = await client.PostAsJsonAsync("/api/tasks", new TaskWriteRequest { Title = "Keep" }, JsonOptions);
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
        using var invalid = await client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new { title = "Replacement", createdAt = "untrusted" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(task, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}", JsonOptions));
        using var missingUpdate = await client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}",
            new TaskWriteRequest { Title = "Missing" }, JsonOptions);
        using var missingDelete = await client.DeleteAsync($"/api/tasks/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missingUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [PostgresFact]
    public async Task FiltersAndNoOpUpdates_PreserveTheExistingContract()
    {
        var clock = new FixedClock();
        TaskResponse original;
        await using (var context = OpenContext())
        {
            var service = new PostgresTaskService(context, new TestCurrentUser(), clock);
            original = await service.CreateAsync(new TaskWriteRequest
            {
                Title = "Today high", Priority = TaskPriority.High, DueDate = new DateOnly(2026, 11, 14),
                IsCompleted = true, CategoryName = "Work"
            }, default);
            var noOp = await service.UpdateAsync(original.Id, new TaskWriteRequest
            {
                Title = " Today high ", Priority = TaskPriority.High, DueDate = original.DueDate,
                IsCompleted = true, CategoryName = " work "
            }, default);
            Assert.Equal(original, noOp);
            await service.CreateAsync(new TaskWriteRequest { Title = "Undated" }, default);
        }

        await using var reopened = OpenContext();
        var readService = new PostgresTaskService(reopened, new TestCurrentUser(), clock);
        Assert.Equal(2, (await readService.GetTasksAsync(TaskFilter.All, default)).Count);
        Assert.Equal("Undated", Assert.Single(await readService.GetTasksAsync(TaskFilter.Pending, default)).Title);
        foreach (var filter in new[] { TaskFilter.Completed, TaskFilter.DueToday, TaskFilter.HighPriority })
            Assert.Equal(original.Id, Assert.Single(await readService.GetTasksAsync(filter, default)).Id);
    }

    [PostgresFact]
    public async Task DatabaseConstraints_RejectDuplicateCategoryKeysAndBlankTitles()
    {
        await using var context = OpenContext();
        await new PostgresTaskService(context, new TestCurrentUser()).CreateAsync(new TaskWriteRequest { Title = "Keep", CategoryName = "Work" }, default);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Categories" ("Id", "Name", "NameKey", "OwnerId") VALUES ({id}, 'work', 'WORK', {TestAuthentication.UserId});
            """));
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"Tasks\" SET \"Title\" = ' ';"));
        Assert.Equal("Keep", (await context.Tasks.AsNoTracking().SingleAsync()).Title);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("PG test +8", TimeSpan.FromHours(8), "PG test +8", "PG test +8");
    }
}
