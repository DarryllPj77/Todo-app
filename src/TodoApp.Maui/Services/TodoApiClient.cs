using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoApp.Domain.Enums;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed class TodoApiClient(HttpClient http, ClientSession session)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false) }
    };

    public async Task SignInAsync(string email, string password, bool register, CancellationToken cancellationToken = default)
    {
        var origin = session.Endpoint;
        var version = await session.SignOutAsync();
        session.EnsureVersion(version);
        if (origin != session.Endpoint) throw new ApiClientException("The API address changed. Check it before signing in.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, register ? "api/auth/register" : "api/auth/login"))
        {
            Content = JsonContent.Create(new { email, password })
        };
        using var response = await SendAsync(request, cancellationToken);
        await CheckResponseAsync(response, version, authenticated: false);
        var result = await ReadAsync<ApiAuthResponse>(response, cancellationToken);
        await session.SaveAsync(result, origin, version);
    }

    public async Task VerifySessionAsync(CancellationToken cancellationToken = default)
    {
        var version = session.Version;
        var result = await ProtectedAsync<ApiUser>(HttpMethod.Get, "api/auth/me", null, cancellationToken, allowUnverified: true);
        session.EnsureVersion(version);
        if (session.Current?.User.Id != result.Id)
        {
            await session.SignOutAsync();
            throw new ApiClientException("The saved account did not match the API response. Sign in again.");
        }
        session.Verify(result, version);
    }

    public Task<ApiTask[]> GetTasksAsync(TaskFilter filter = TaskFilter.All, CancellationToken cancellationToken = default) =>
        ProtectedAsync<ApiTask[]>(HttpMethod.Get, $"api/tasks?filter={filter}", null, cancellationToken);
    public Task<ApiTask> CreateAsync(ApiTaskWrite task, CancellationToken cancellationToken = default) =>
        ProtectedAsync<ApiTask>(HttpMethod.Post, "api/tasks", task, cancellationToken);
    public Task<ApiTask> UpdateAsync(Guid id, ApiTaskWrite task, CancellationToken cancellationToken = default) =>
        ProtectedAsync<ApiTask>(HttpMethod.Put, $"api/tasks/{id}", task, cancellationToken);
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ProtectedAsync<object>(HttpMethod.Delete, $"api/tasks/{id}", null, cancellationToken);

    private async Task<T> ProtectedAsync<T>(HttpMethod method, string path, ApiTaskWrite? body,
        CancellationToken cancellationToken, bool allowUnverified = false)
    {
        var version = session.Version;
        var saved = await session.RequireAsync(allowUnverified);
        session.EnsureVersion(version);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(saved.Origin), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saved.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(request, cancellationToken);
        await CheckResponseAsync(response, version, authenticated: true);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        var result = await ReadAsync<T>(response, cancellationToken);
        session.EnsureVersion(version);
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return await http.SendAsync(request, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiClientException("The API request timed out. Refresh before retrying a write; it may already have been saved.");
        }
        catch (HttpRequestException)
        {
            throw new ApiClientException("Couldn't reach the API. Check its address, network, and trusted HTTPS certificate. Refresh before retrying a write.");
        }
    }

    private async Task CheckResponseAsync(HttpResponseMessage response, int expectedVersion, bool authenticated)
    {
        session.EnsureVersion(expectedVersion);
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated) await session.SignOutAsync();
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => authenticated ? "The session was rejected. Sign in again." : "Invalid email or password.",
            HttpStatusCode.Forbidden => "This account does not have permission for that operation.",
            HttpStatusCode.BadRequest => "Check the entered fields and try again.",
            HttpStatusCode.NotFound => "This server task no longer exists. Refresh the server list.",
            HttpStatusCode.Conflict => "Registration could not be completed with those details.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute before trying again.",
            _ => "The API couldn't finish the request. Refresh before retrying a write; it may already have been saved."
        };
        throw new ApiClientException(message, response.StatusCode);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new ApiClientException("The API returned an empty response.");
        }
        catch (JsonException) { throw new ApiClientException("The API returned an unreadable response. Refresh before retrying a write."); }
    }
}
