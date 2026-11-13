using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Domain.Enums;

namespace TodoApp.Tests;

public sealed class ApiTasksTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false) }
    };
    private readonly WebApplicationFactory<Program> factory = new ApiTestFactory()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(new ServerClock())));
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task TaskRoutes_SupportCreateReadFullUpdateAndDeleteWithExpectedStatusCodes()
    {
        using var response = await client.PostAsJsonAsync("/api/tasks", new TaskWriteRequest
        {
            Title = "  Learn REST  ", Description = " Notes ", DueDate = new DateOnly(2026, 11, 20),
            Priority = TaskPriority.High, CategoryName = " Learning "
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal($"/api/tasks/{created.Id}", response.Headers.Location!.AbsolutePath);
        var read = (await client.GetFromJsonAsync<TaskResponse>(response.Headers.Location, JsonOptions))!;
        Assert.Equal(created, read);
        Assert.Equal("Learn REST", read.Title);
        Assert.Equal("Notes", read.Description);
        Assert.Equal("Learning", read.CategoryName);
        Assert.NotNull(read.CategoryId);
        Assert.False(read.IsCompleted);
        Assert.Equal(TimeSpan.Zero, read.CreatedAt.Offset);

        using var updatedResponse = await client.PutAsJsonAsync($"/api/tasks/{created.Id}",
            new TaskWriteRequest { Title = "Updated", IsCompleted = true }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.IsCompleted);
        Assert.Equal("Updated", updated.Title);
        Assert.Null(updated.Description);
        Assert.Null(updated.DueDate);
        Assert.Null(updated.CategoryId);
        Assert.Null(updated.CategoryName);
        Assert.Equal(TaskPriority.Normal, updated.Priority);

        using var reopenedResponse = await client.PutAsJsonAsync($"/api/tasks/{created.Id}",
            new TaskWriteRequest { Title = "Updated" }, JsonOptions);
        Assert.False((await reopenedResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!.IsCompleted);
        using var delete = await client.DeleteAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal("", await delete.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("put")]
    [InlineData("delete")]
    public async Task MissingTasks_ReturnNotFoundProblemDetails(string operation)
    {
        var path = $"/api/tasks/{Guid.NewGuid()}";
        using var response = operation switch
        {
            "get" => await client.GetAsync(path),
            "put" => await client.PutAsJsonAsync(path, new TaskWriteRequest { Title = "Missing" }, JsonOptions),
            _ => await client.DeleteAsync(path)
        };
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(404, problem!.Status);
        Assert.Empty((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"title\":\"  \"}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"Valid\",\"priority\":\"Unknown\"}")]
    [InlineData("{\"title\":\"Valid\",\"priority\":99}")]
    [InlineData("{\"title\":\"Valid\",\"dueDate\":\"not-a-date\"}")]
    public async Task InvalidJsonOrFields_ReturnValidationProblemsWithoutCreatingTasks(string json)
    {
        using var response = await client.PostAsync("/api/tasks", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(400, problem!.Status);
        Assert.NotEmpty(problem.Errors);
        Assert.Empty((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Theory]
    [InlineData("title", 201)]
    [InlineData("description", 4001)]
    [InlineData("categoryName", 101)]
    public async Task OversizedFields_AreRejectedOnCreateAndUpdateWithoutPartialEdits(string field, int length)
    {
        var original = await CreateAsync(new TaskWriteRequest { Title = "Keep", CategoryName = "Original" });
        var values = new Dictionary<string, object?> { ["title"] = "Replacement", [field] = new string('x', length) };
        using var post = await client.PostAsJsonAsync("/api/tasks", values);
        using var put = await client.PutAsJsonAsync($"/api/tasks/{original.Id}", values);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var read = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{original.Id}", JsonOptions);
        Assert.Equal(original, read);
        Assert.Single((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("categoryId")]
    [InlineData("category")]
    public async Task ServerManagedOrNavigationProperties_AreRejectedOnCreateAndUpdate(string field)
    {
        var original = await CreateAsync(new TaskWriteRequest { Title = "Keep" });
        var payload = new Dictionary<string, object?> { ["title"] = "Replacement", [field] = "untrusted" };
        using var post = await client.PostAsJsonAsync("/api/tasks", payload);
        using var put = await client.PutAsJsonAsync($"/api/tasks/{original.Id}", payload);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal(original, await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{original.Id}", JsonOptions));
        Assert.Single((await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Theory]
    [InlineData("All", "Completed high,No date,Overdue,Today high,Tomorrow")]
    [InlineData("Pending", "No date,Overdue,Today high,Tomorrow")]
    [InlineData("Completed", "Completed high")]
    [InlineData("DueToday", "Completed high,Today high")]
    [InlineData("HighPriority", "Completed high,Today high")]
    public async Task QueryFilters_UseSharedRulesAndServerLocalDate(string filter, string expected)
    {
        var today = new DateOnly(2026, 11, 14);
        await CreateAsync(new TaskWriteRequest { Title = "Tomorrow", DueDate = today.AddDays(1) });
        await CreateAsync(new TaskWriteRequest { Title = "Today high", DueDate = today, Priority = TaskPriority.High });
        await CreateAsync(new TaskWriteRequest { Title = "No date" });
        await CreateAsync(new TaskWriteRequest { Title = "Overdue", DueDate = today.AddDays(-1) });
        await CreateAsync(new TaskWriteRequest { Title = "Completed high", DueDate = today, Priority = TaskPriority.High, IsCompleted = true });

        var results = await client.GetFromJsonAsync<TaskResponse[]>($"/api/tasks?filter={filter}", JsonOptions);
        Assert.Equal(expected.Split(','), results!.Select(task => task.Title));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("Unknown")]
    public async Task InvalidFilter_ReturnsBadRequest(string filter)
    {
        using var response = await client.GetAsync($"/api/tasks?filter={filter}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotEmpty(problem!.Errors);
    }

    [Fact]
    public async Task ResponseIsFlat_AndCategoryNamesAreReusedWithoutExposingTaskGraphs()
    {
        var first = await CreateAsync(new TaskWriteRequest { Title = "First", CategoryName = "Work" });
        var second = await CreateAsync(new TaskWriteRequest { Title = "Second", CategoryName = " work " });
        Assert.Equal(first.CategoryId, second.CategoryId);
        Assert.Equal("Work", second.CategoryName);
        using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{first.Id}"));
        Assert.False(body.RootElement.TryGetProperty("category", out _));
        Assert.False(body.RootElement.TryGetProperty("tasks", out _));
        Assert.Equal("Normal", body.RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task ParallelRequests_RetainAllTasksAndShareOneCategory()
    {
        var writes = Enumerable.Range(1, 8).Select(number => CreateAsync(
            new TaskWriteRequest { Title = $"Task {number}", CategoryName = "Work" }));
        var created = await Task.WhenAll(writes);
        Assert.Equal(8, created.Select(task => task.Id).Distinct().Count());
        Assert.Single(created.Select(task => task.CategoryId).Distinct());
        Assert.Equal(8, (await client.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!.Length);
    }

    [Fact]
    public async Task AnotherServerInstance_StartsEmpty()
    {
        await CreateAsync(new TaskWriteRequest { Title = "Not durable" });
        await using var otherFactory = new ApiTestFactory();
        using var other = otherFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Empty((await other.GetFromJsonAsync<TaskResponse[]>("/api/tasks", JsonOptions))!);
    }

    [Fact]
    public async Task NonDevelopmentServer_RefusesToStart()
    {
        await using var production = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var exception = Assert.Throws<InvalidOperationException>(() => production.CreateClient());
        Assert.Contains("Development only", exception.Message);
    }

    [Fact]
    public async Task CanceledServiceCalls_DoNotChangeServerData()
    {
        var service = new InMemoryTaskService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        var request = new TaskWriteRequest { Title = "Canceled" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(request, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(Guid.NewGuid(), request, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetTasksAsync(TaskFilter.All, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(Guid.NewGuid(), token));
        Assert.Empty(await service.GetTasksAsync(TaskFilter.All, CancellationToken.None));
    }

    private async Task<TaskResponse> CreateAsync(TaskWriteRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/tasks", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
    }

    private sealed class ServerClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("Server test +8", TimeSpan.FromHours(8), "Server test +8", "Server test +8");
    }
}
