using System.Net;
using System.Text.Json;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class ServerWorkspaceTests
{
    private static ApiTask Task(string title = "Server task") => new(Guid.NewGuid(), title, "Notes", false,
        new DateOnly(2026, 11, 20), TaskPriority.High, null, "Work", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Logout_ClearsServerRowsAndDraftsWithoutChangingDeviceLocalTasks()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        var local = new InMemoryLocalTaskService();
        await local.CreateAsync(new TaskDetails("Device only"));
        var serverTask = Task();
        harness.Handler.Response = (_, _) => System.Threading.Tasks.Task.FromResult(ClientHarness.Json(new[] { serverTask }));
        await workspace.RefreshCommand.ExecuteAsync();
        workspace.SelectedTask = Assert.Single(workspace.Tasks);
        workspace.Title = "Unsaved server edit";

        await harness.Session.SignOutAsync();

        Assert.Empty(workspace.Tasks);
        Assert.Null(workspace.SelectedTask);
        Assert.Equal("", workspace.Title);
        Assert.False(workspace.SaveCommand.CanExecute(null));
        Assert.Equal("Device only", Assert.Single(await local.GetTasksAsync()).Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateResponseFromOldAccount_CannotPopulateRowsOrLogOutTheNewAccount(bool unauthorized)
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Handler.Response = (request, _) => request.Method == HttpMethod.Get
            ? oldResponse.Task : System.Threading.Tasks.Task.FromResult(ClientHarness.Json(harness.Account));
        var reading = workspace.RefreshCommand.ExecuteAsync();
        Assert.True(workspace.IsBusy);
        var second = harness.Auth("second-token", "second@example.invalid");
        harness.Account = second;
        await harness.LoginAsync();
        oldResponse.SetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : ClientHarness.Json(new[] { Task("Old private data") }));
        await reading;

        Assert.Equal(second.User.Id, harness.Session.Current!.User.Id);
        Assert.Empty(workspace.Tasks);
        Assert.Equal("", workspace.ErrorMessage);
        Assert.True(workspace.IsAvailable);
    }

    [Fact]
    public async Task UncertainWrite_RequiresAnExplicitSuccessfulRefreshBeforeRetry()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        workspace.Title = "Possibly saved";
        harness.Handler.Response = (_, _) => System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new HttpRequestException());
        var before = harness.Handler.Requests.Count;

        await workspace.SaveCommand.ExecuteAsync();
        await workspace.SaveCommand.ExecuteAsync();

        Assert.Equal(before + 1, harness.Handler.Requests.Count);
        Assert.True(workspace.NeedsRefresh);
        Assert.Equal("Possibly saved", workspace.Title);
        Assert.False(workspace.SaveCommand.CanExecute(null));
        harness.Handler.Response = (_, _) => System.Threading.Tasks.Task.FromResult(ClientHarness.Json(Array.Empty<ApiTask>()));
        await workspace.RefreshCommand.ExecuteAsync();
        Assert.False(workspace.NeedsRefresh);
        Assert.True(workspace.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_UsesEditableFieldsAndKeepsServerOwnedFieldsOutOfTheRequest()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        var saved = Task("New server title");
        harness.Handler.Response = (request, _) => System.Threading.Tasks.Task.FromResult(request.Method == HttpMethod.Post
            ? ClientHarness.Json(saved, HttpStatusCode.Created) : ClientHarness.Json(new[] { saved }));
        workspace.Title = "New server title";
        workspace.Description = "Details";
        workspace.CategoryName = "Work";
        workspace.Priority = TaskPriority.High;
        workspace.DueDate = new DateTime(2026, 11, 20);
        await workspace.SaveCommand.ExecuteAsync();

        var request = harness.Handler.Requests.Single(item => item.Uri.AbsolutePath == "/api/tasks" && item.Method == HttpMethod.Post);
        using var json = JsonDocument.Parse(request.Body!);
        Assert.Equal("High", json.RootElement.GetProperty("priority").GetString());
        Assert.Equal("2026-11-20", json.RootElement.GetProperty("dueDate").GetString());
        Assert.False(json.RootElement.TryGetProperty("id", out _));
        Assert.False(json.RootElement.TryGetProperty("ownerId", out _));
        Assert.False(json.RootElement.TryGetProperty("createdAt", out _));
        Assert.Single(workspace.Tasks);
        Assert.Equal("", workspace.Title);
    }

    [Fact]
    public async Task FailedPostSaveRefresh_CannotReplayTheCreateDraft()
    {
        using var harness = new ClientHarness();
        await harness.LoginAsync();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        harness.Handler.Response = (request, _) => request.Method == HttpMethod.Post
            ? System.Threading.Tasks.Task.FromResult(ClientHarness.Json(Task(), HttpStatusCode.Created))
            : System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new HttpRequestException());
        workspace.Title = "Only create once";
        await workspace.SaveCommand.ExecuteAsync();
        await workspace.SaveCommand.ExecuteAsync();
        Assert.Equal("", workspace.Title);
        Assert.True(workspace.NeedsRefresh);
        Assert.Single(harness.Handler.Requests, request => request.Uri.AbsolutePath == "/api/tasks" && request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AccountValidationAndFailedStorage_ClearPasswordAndPreserveOfflineAvailability()
    {
        using var harness = new ClientHarness();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        using var account = new AccountViewModel(harness.Session, harness.Api, workspace);
        account.Email = "invalid";
        account.Password = "short";
        await account.RegisterCommand.ExecuteAsync();
        Assert.Empty(harness.Handler.Requests);
        Assert.Equal("", account.Password);
        Assert.True(account.HasError);
        account.Email = harness.Account.User.Email;
        account.Password = "Valid local test password";
        harness.Storage.FailWrite = true;
        await account.LoginCommand.ExecuteAsync();
        Assert.Equal("", account.Password);
        Assert.False(account.HasSession);
        Assert.True(account.HasError);
        Assert.False(account.IsBusy);
    }

    [Fact]
    public async Task AccountLogin_LoadsServerTasksAndEndpointChangesClearThem()
    {
        using var harness = new ClientHarness();
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        using var account = new AccountViewModel(harness.Session, harness.Api, workspace);
        var task = Task();
        harness.Handler.Response = (request, _) => System.Threading.Tasks.Task.FromResult(request.Method == HttpMethod.Post
            ? ClientHarness.Json(harness.Account) : ClientHarness.Json(new[] { task }));
        account.Email = harness.Account.User.Email;
        account.Password = "The captured test password";
        await account.LoginCommand.ExecuteAsync();
        Assert.True(account.HasSession);
        Assert.Equal("", account.Password);
        Assert.Contains("The captured test password", harness.Handler.Requests[0].Body);
        Assert.Single(workspace.Tasks);
        account.Endpoint = "https://new-api.example.invalid:7443";
        await account.SaveEndpointCommand.ExecuteAsync();
        Assert.False(account.HasSession);
        Assert.Empty(workspace.Tasks);
        Assert.Equal("https://new-api.example.invalid:7443/", account.ActiveEndpoint);
    }

    [Fact]
    public async Task InvalidSavedEndpoint_CanBeCorrectedFromTheAccountScreen()
    {
        using var harness = new ClientHarness();
        harness.Settings.ApiBaseUrl = "invalid";
        using var workspace = new ServerWorkspaceViewModel(harness.Api, harness.Session, new ClientNavigation());
        using var account = new AccountViewModel(harness.Session, harness.Api, workspace);
        account.Endpoint = "https://localhost:7114";
        await account.SaveEndpointCommand.ExecuteAsync();
        Assert.False(account.HasError);
        Assert.Equal("https://localhost:7114/", account.ActiveEndpoint);
    }
}
