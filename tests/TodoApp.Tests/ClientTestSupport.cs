using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoApp.Domain.Enums;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

internal sealed class MemoryClientSettings : IClientSettings
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7114/";
    public bool RestorationBlocked { get; set; }
}

internal sealed class MemoryTokenStore : ISecureTokenStore
{
    public string? Value { get; set; }
    public bool FailRead { get; set; }
    public bool FailWrite { get; set; }
    public bool FailClear { get; set; }
    public int ReadCount { get; private set; }
    public TaskCompletionSource? ReadGate { get; set; }
    public TaskCompletionSource? WriteGate { get; set; }
    public async Task<string?> GetAsync()
    {
        ReadCount++;
        if (FailRead) throw new IOException("Test secure read failure");
        var snapshot = Value;
        if (ReadGate is not null) await ReadGate.Task;
        return snapshot;
    }
    public async Task SetAsync(string value)
    {
        if (FailWrite) throw new IOException("Test secure write failure");
        if (WriteGate is not null) await WriteGate.Task;
        Value = value;
    }
    public void Clear()
    {
        if (FailClear) throw new IOException("Test secure clear failure");
        Value = null;
    }
}

internal sealed class ClientClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class ClientHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Response { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    public List<(HttpMethod Method, Uri Uri, string? Token, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return await Response(request, cancellationToken);
    }
}

internal sealed class ClientHarness : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false) }
    };
    public MemoryClientSettings Settings { get; } = new();
    public MemoryTokenStore Storage { get; } = new();
    public ClientClock Clock { get; } = new();
    public ClientHandler Handler { get; } = new();
    public ClientSession Session { get; }
    public TodoApiClient Api { get; }
    public HttpClient Http { get; }
    public ApiAuthResponse Account { get; set; }

    public ClientHarness()
    {
        Account = Auth("test-access-token", "first@example.invalid");
        Session = new ClientSession(Settings, Storage, Clock);
        Http = new HttpClient(Handler);
        Api = new TodoApiClient(Http, Session);
        Handler.Response = (request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/auth/login" or "/api/auth/register" => (object)Account,
            "/api/auth/me" => Account.User,
            _ => Array.Empty<ApiTask>()
        }));
    }

    public ApiAuthResponse Auth(string token, string email) => new(token, "Bearer", Clock.Now.AddMinutes(15), new ApiUser(Guid.NewGuid(), email));
    public Task LoginAsync() => Api.SignInAsync(Account.User.Email, "Test password is never saved", register: false);
    public static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value, options: JsonOptions) };
    public void Dispose() => Http.Dispose();
}

internal sealed class ClientNavigation : ITaskNavigation
{
    public bool Confirm { get; set; }
    public Task OpenRecurrenceAsync() => Task.CompletedTask;
    public Func<Task<bool>>? RecurrenceConfirmation { get; set; }
    public Task<bool> ConfirmRecurrenceConflictAsync(string title) => RecurrenceConfirmation?.Invoke() ?? Task.FromResult(Confirm);
    public Func<Task<bool>>? OccurrenceConfirmation { get; set; }
    public Task<bool> ConfirmOccurrenceAsync(string title, DateOnly current, DateOnly next) => OccurrenceConfirmation?.Invoke() ?? Task.FromResult(Confirm);
    public Task OpenPlanningAsync() => Task.CompletedTask;
    public Func<Task<bool>>? PlanningConfirmation { get; set; }
    public Task<bool> ConfirmPlanningConflictAsync(string title) => PlanningConfirmation?.Invoke() ?? Task.FromResult(Confirm);
    public Task OpenDependenciesAsync() => Task.CompletedTask;
    public Func<Task<bool>>? DependencyConfirmation { get; set; }
    public Task<bool> ConfirmDependencyConflictAsync(string title) => DependencyConfirmation?.Invoke() ?? Task.FromResult(Confirm);
    public Task OpenHierarchyAsync() => Task.CompletedTask;
    public Task<bool> ConfirmHierarchyConflictAsync(string title) => Task.FromResult(false);
    public Task OpenTaskViewsAsync() => Task.CompletedTask;
    public Task<bool> ConfirmDeleteViewAsync(string name) => Task.FromResult(Confirm);
    public Task OpenRemindersAsync() => Task.CompletedTask;
    public Task OpenSyncAsync() => Task.CompletedTask;
    public Task OpenAccountAsync() => Task.CompletedTask;
    public Task EditTaskAsync(Guid? id) => Task.CompletedTask;
    public Task CloseEditorAsync() => Task.CompletedTask;
    public Task<bool> ConfirmDeleteAsync(string title) => Task.FromResult(Confirm);
}
