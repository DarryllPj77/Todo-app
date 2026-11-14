using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TodoApp.Domain.Calendar;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed partial class GoogleCalendarApi(HttpClient http)
{
    private const string Root = "https://www.googleapis.com/calendar/v3/";
    public async Task<IReadOnlyList<GoogleCalendarChoice>> CalendarsAsync(GoogleGrant grant, Action guard, CancellationToken ct)
    {
        var result = new Dictionary<string, GoogleCalendarChoice>(StringComparer.Ordinal); string? page = null;
        var pages = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            guard();
            using var response = await SendAsync(grant, HttpMethod.Get, "users/me/calendarList?minAccessRole=owner&maxResults=100" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), null, null, ct);
            using var json = await DocumentAsync(response, ct); guard();
            if (json.RootElement.TryGetProperty("items", out var items)) foreach (var item in items.EnumerateArray())
            {
                if (Text(item, "accessRole") == "owner" && !True(item, "deleted"))
                {
                    var id = Required(item, "id", 1024); var name = Text(item, "summaryOverride") ?? Text(item, "summary") ?? id;
                    result[id] = new(id, name.Length <= 1024 ? name : name[..1024]);
                }
            }
            page = Text(json.RootElement, "nextPageToken");
            if (page is not null && (page.Length > 4096 || !pages.Add(page) || pages.Count > 20))
                throw new GoogleEventConflictException("The calendar list was incomplete or repeated. No destination was selected.");
        } while (page is not null);
        return result.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id).ToArray();
    }
    public async Task RequireOwnerAsync(GoogleGrant grant, string calendarId, Action guard, CancellationToken ct)
    {
        guard(); using var response = await SendAsync(grant, HttpMethod.Get, "users/me/calendarList/" + Escape(calendarId), null, null, ct);
        using var json = await DocumentAsync(response, ct); guard();
        if (Required(json.RootElement, "id", 1024) != calendarId || Text(json.RootElement, "accessRole") != "owner" || True(json.RootElement, "deleted"))
            throw new GoogleEventConflictException("The destination is no longer an owned calendar. No event was changed.");
    }
    public async Task<GoogleEventResource?> GetAsync(GoogleGrant grant, GoogleEventLink link, Action guard, CancellationToken ct)
    {
        guard(); using var response = await SendAsync(grant, HttpMethod.Get, EventPath(link), null, null, ct, allowMissing: true);
        guard(); if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        using var json = await DocumentAsync(response, ct); guard();
        if (Text(json.RootElement, "status") == "cancelled") return null;
        return ParseEvent(json.RootElement, link);
    }
    public async Task<GoogleEventResource> WriteAsync(GoogleGrant grant, GoogleEventLink link, bool create, Action guard, CancellationToken ct)
    {
        var snapshot = link.Desired;
        var body = new Dictionary<string, object> { ["summary"] = snapshot.Title,
            ["start"] = new { dateTime = snapshot.StartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
            ["end"] = new { dateTime = snapshot.EndUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) } };
        if (create)
        {
            body["id"] = link.EventId; body["eventType"] = "default"; body["visibility"] = "private"; body["transparency"] = "opaque";
            body["reminders"] = new { useDefault = false };
            body["extendedProperties"] = new { @private = new Dictionary<string, string> { ["todoLink"] = link.LinkId.ToString("N"), ["todoProof"] = link.Proof } };
        }
        guard(); using var response = await SendAsync(grant, create ? HttpMethod.Post : HttpMethod.Patch,
            (create ? "calendars/" + Escape(link.CalendarId) + "/events" : EventPath(link)) + "?sendUpdates=none", body, create ? null : link.ETag, ct);
        using var json = await DocumentAsync(response, ct); guard();
        return ParseEvent(json.RootElement, link);
    }
    public async Task DeleteAsync(GoogleGrant grant, GoogleEventLink link, Action guard, CancellationToken ct)
    {
        guard(); using var response = await SendAsync(grant, HttpMethod.Delete, EventPath(link) + "?sendUpdates=none", null, link.ETag, ct, allowMissing: true); guard();
    }
    private async Task<HttpResponseMessage> SendAsync(GoogleGrant grant, HttpMethod method, string path, object? body, string? etag, CancellationToken ct, bool allowMissing = false)
    {
        using var request = new HttpRequestMessage(method, Root + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", grant.AccessToken);
        if (etag is not null)
        {
            if (!EntityTagHeaderValue.TryParse(etag, out var tag) || tag.IsWeak || tag.Tag == "*") throw new GoogleEventConflictException("The event revision is not safe for a conditional write.");
            request.Headers.IfMatch.Add(tag);
        }
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || allowMissing && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return response;
        var status = response.StatusCode;
        var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } at ? at - DateTimeOffset.UtcNow : (TimeSpan?)null);
        response.Dispose();
        if (status == HttpStatusCode.Unauthorized) throw new GoogleAuthorizationException("Google access expired or was revoked. Check/reconnect the Google connection.", true);
        throw new GoogleCalendarHttpException(status, delay);
    }
    private static GoogleEventResource ParseEvent(JsonElement json, GoogleEventLink link)
    {
        if (Required(json, "id", 1024) != link.EventId || Text(json, "eventType") is not (null or "default") || json.TryGetProperty("recurringEventId", out _)
            || NonemptyArray(json, "recurrence") || NonemptyArray(json, "attendees") || json.TryGetProperty("conferenceData", out _))
            throw new GoogleEventConflictException("The linked event is recurring, shared with attendees, or no longer a simple Todo event. No overwrite/deletion was allowed.");
        if (!json.TryGetProperty("extendedProperties", out var properties) || !properties.TryGetProperty("private", out var own)
            || Text(own, "todoLink") != link.LinkId.ToString("N") || Text(own, "todoProof") != link.Proof)
            throw new GoogleEventConflictException("The event ownership markers do not match this link. No event was overwritten or deleted.");
        var etag = Required(json, "etag", 1024);
        if (!EntityTagHeaderValue.TryParse(etag, out var tag) || tag.IsWeak || tag.Tag == "*") throw new GoogleEventConflictException("Google did not return a usable event revision.");
        return new(link.EventId, etag, link.Proof, link.LinkId.ToString("N"), Text(json, "summary") ?? "", Instant(json, "start"), Instant(json, "end"));
    }
    private static DateTimeOffset Instant(JsonElement json, string field)
    {
        if (!json.TryGetProperty(field, out var time) || Text(time, "dateTime") is not { } value
            || !(value.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(value, "[+-][0-9]{2}:[0-9]{2}$"))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            throw new GoogleEventConflictException("The linked event is all-day or has unreadable time information.");
        return instant.ToUniversalTime();
    }
    private static bool NonemptyArray(JsonElement json, string field) => json.TryGetProperty(field, out var value) && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0);
    private static bool True(JsonElement json, string field) => json.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement json, string field) => json.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Required(JsonElement json, string field, int max)
    {
        var value = Text(json, field);
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new GoogleEventConflictException("Google returned incomplete calendar/event metadata.");
        return value;
    }
    private static string Escape(string id) => Uri.EscapeDataString(id);
    private static string EventPath(GoogleEventLink link) => "calendars/" + Escape(link.CalendarId) + "/events/" + Escape(link.EventId);
    private static Task<JsonDocument> DocumentAsync(HttpResponseMessage response, CancellationToken ct) => ReadAsync(response, ct);
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
}
