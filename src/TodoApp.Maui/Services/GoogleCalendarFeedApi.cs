using System.Net;
using System.Text.Json;
using TodoApp.Domain.Calendar;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed partial class GoogleCalendarApi
{
    public async Task<GoogleFeedBatch> ChangesAsync(GoogleGrant grant, GoogleCalendarFeed feed, IReadOnlyList<GoogleEventLink> cohort,
        Action guard, DateTimeOffset observedUtc, CancellationToken ct)
    {
        var original = feed.Cursor; var cursor = original; var reset = false;
        while (true)
        {
            try { return await ReadChangePagesAsync(grant, feed.Binding.CalendarId, cursor, cohort, guard, observedUtc, reset, ct); }
            catch (GoogleCalendarHttpException error) when (error.Status == HttpStatusCode.Gone && cursor is not null && !reset)
            {
                guard(); cursor = null; reset = true; // Discard only this in-memory batch, never local tasks or journal intent.
            }
        }
    }
    private async Task<GoogleFeedBatch> ReadChangePagesAsync(GoogleGrant grant, string calendarId, string? cursor,
        IReadOnlyList<GoogleEventLink> cohort, Action guard, DateTimeOffset observedUtc, bool reset, CancellationToken ct)
    {
        var tracked = cohort.Where(l => !l.Pending).ToDictionary(l => l.EventId, StringComparer.Ordinal);
        var notices = new Dictionary<Guid, GoogleChangeNotice>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal); string? page = null; var count = 0;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            guard();
            var path = "calendars/" + Escape(calendarId) + "/events?maxResults=100&singleEvents=false&showDeleted=true"
                + (cursor is null ? "" : "&syncToken=" + Uri.EscapeDataString(cursor)) + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var response = await SendAsync(grant, HttpMethod.Get, path, null, null, ct);
            using var document = await DocumentAsync(response, ct); guard(); var json = document.RootElement;
            if (Text(json, "accessRole") is { } role && role != "owner") throw new GoogleEventConflictException("Calendar owner access changed during the scan.");
            if (json.TryGetProperty("items", out var items))
            {
                if (items.ValueKind != JsonValueKind.Array) throw new GoogleEventConflictException("Google returned an incomplete event page.");
                foreach (var item in items.EnumerateArray())
                {
                    if (++count > 10000) throw new GoogleEventConflictException("This calendar exceeds the bounded scan limit. Use a smaller dedicated calendar; the old cursor was retained.");
                    var id = Required(item, "id", 1024);
                    if (!seen.Add(id)) throw new GoogleEventConflictException("Google repeated an event across pages. No partial cursor was saved.");
                    if (!tracked.TryGetValue(id, out var link)) continue; // Unlinked payloads are never cached.
                    GoogleChangeNotice? notice = null;
                    if (Text(item, "status") == "cancelled")
                    {
                        if (link.Review?.Kind != GoogleReviewKind.Missing) notice = new(GoogleNoticeKind.Missing, "", observedUtc, "Google deletion detected. Load a review before accepting it; the local block remains.");
                    }
                    else
                    {
                        try
                        {
                            var remote = ParseEvent(item, link);
                            var alreadyReviewed = link.Review?.Kind == GoogleReviewKind.Present && link.Review.Remote == new GoogleReviewedFields(remote.ETag, remote.Title, remote.StartUtc, remote.EndUtc);
                            if (!alreadyReviewed && (remote.ETag != link.ETag || link.Published is null || !remote.Matches(link.Published)))
                                notice = new(GoogleNoticeKind.Changed, remote.ETag, observedUtc, "Google fields/version changed. Load a fresh review before choosing a side.");
                        }
                        catch (GoogleEventConflictException)
                        {
                            if (link.Review?.Kind != GoogleReviewKind.Unsafe) notice = new(GoogleNoticeKind.Unsafe, "", observedUtc, "Google ownership/type/time metadata changed. Review it; no forced overwrite or deletion is allowed.");
                        }
                    }
                    if (notice is not null) notices[link.BlockId] = notice;
                }
            }
            page = Text(json, "nextPageToken"); var next = Text(json, "nextSyncToken");
            if (page is not null)
            {
                if (!FeedToken(page) || next is not null || !pages.Add(page)) throw new GoogleEventConflictException("Google pagination was repeated or inconsistent. No partial cursor was saved.");
                continue;
            }
            if (!FeedToken(next)) throw new GoogleEventConflictException("Google did not return the final sync cursor. The prior cursor was retained.");
            if (cursor is null)
            {
                foreach (var link in tracked.Values.Where(l => !seen.Contains(l.EventId) && l.Review?.Kind != GoogleReviewKind.Missing))
                    notices[link.BlockId] = new(GoogleNoticeKind.Missing, "", observedUtc, "Linked event absent from a complete Google snapshot. Load a review; nothing was deleted locally.");
            }
            return new(next!, cursor is null, reset, notices);
        }
        throw new GoogleEventConflictException("Google pagination exceeded the bounded page limit. No partial cursor was saved.");
    }
    private static bool FeedToken(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 8192 && !value.Any(char.IsControl);
}
