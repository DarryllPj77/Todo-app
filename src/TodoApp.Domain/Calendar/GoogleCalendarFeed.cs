namespace TodoApp.Domain.Calendar;

public enum GoogleNoticeKind { Changed, Missing, Unsafe }
public sealed record GoogleChangeNotice(GoogleNoticeKind Kind, string ETag, DateTimeOffset DetectedUtc, string Message);

public sealed record GoogleFeedBinding(string Scope, string Subject, string ClientId, string CalendarId, string CalendarName)
{
    public bool Matches(GoogleFeedBinding other) => Scope == other.Scope && Subject == other.Subject && ClientId == other.ClientId && CalendarId == other.CalendarId;
    public bool Matches(GoogleEventLink link) => Scope == link.Scope && Subject == link.Subject && ClientId == link.ClientId && CalendarId == link.CalendarId;
}
public sealed record GoogleCalendarFeed(GoogleFeedBinding Binding, long Revision = 0, int QueryVersion = 1, bool Enabled = false,
    string? Cursor = null, DateTimeOffset? LastCheckedUtc = null, int Failures = 0, DateTimeOffset? RetryAfterUtc = null, string Problem = "")
{
    public string Label => $"{Binding.CalendarName} · {(Enabled ? "Checks enabled" : "Checks off")}";
    public string Details => $"Calendar: {Binding.CalendarName} ({Binding.CalendarId})\nLast check: {LastCheckedUtc?.ToString("u") ?? "Never"}\nNext allowed automatic attempt: {RetryAfterUtc?.ToString("u") ?? "Now"}\n{Problem}";
    public override string ToString() => "Google calendar change monitor";
}
public sealed record GoogleFeedBatch(string Cursor, bool Full, bool Reset, IReadOnlyDictionary<Guid, GoogleChangeNotice> Notices);
public sealed record GoogleFeedResult(GoogleCalendarFeed Feed, int Notices, int PendingSkipped, bool Full, bool Reset, bool Skipped = false);
