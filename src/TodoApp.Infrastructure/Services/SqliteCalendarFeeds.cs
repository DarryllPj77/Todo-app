using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;

namespace TodoApp.Infrastructure.Services;

public sealed partial class SqliteCalendarStore
{
    public Task<IReadOnlyList<GoogleCalendarFeed>> FeedsAsync(string scope) => ExecuteAsync(scope, ReadFeeds);
    public async Task<GoogleCalendarFeed> FeedAsync(GoogleFeedBinding binding) =>
        (await FeedsAsync(binding.Scope)).SingleOrDefault(f => f.Binding.Matches(binding)) ?? new(binding);
    public Task<GoogleCalendarFeed> SetFeedEnabledAsync(GoogleCalendarFeed expected, bool enabled) => ExecuteAsync(expected.Binding.Scope, (connection, transaction, _) =>
    {
        RequireFeed(connection, transaction, expected);
        var next = expected with { Revision = checked(expected.Revision + 1), Enabled = enabled, RetryAfterUtc = null, Failures = 0, Problem = "" };
        WriteFeed(connection, transaction, next); return next;
    });
    public Task<GoogleFeedResult> CommitFeedAsync(GoogleCalendarFeed expected, IReadOnlyList<GoogleEventLink> cohort, GoogleFeedBatch batch) =>
        ExecuteAsync(expected.Binding.Scope, (connection, transaction, _) =>
        {
            RequireFeed(connection, transaction, expected);
            var current = ReadLinks(connection, transaction, expected.Binding.Scope).Where(l => expected.Binding.Matches(l)
                && l.State is not (GoogleLinkState.Deleted or GoogleLinkState.Detached)).OrderBy(l => l.BlockId).ToArray();
            if (!current.SequenceEqual(cohort.OrderBy(l => l.BlockId))) throw new CalendarConflictException();
            if (!Opaque(batch.Cursor)) throw new ArgumentException("The complete Google result needs a valid sync cursor.");
            var changed = 0;
            foreach (var pair in batch.Notices)
            {
                var link = current.SingleOrDefault(l => l.BlockId == pair.Key);
                if (link is null || link.Pending) throw new ArgumentException("Only unchanged linked/conflicted records can receive feed notices.");
                var notice = pair.Value;
                if (!Enum.IsDefined(notice.Kind) || notice.ETag is null || notice.ETag.Length > 1024 || notice.ETag.Any(char.IsControl)
                    || notice.Message is null || notice.Message.Length > 1024 || notice.DetectedUtc.Offset != TimeSpan.Zero)
                    throw new ArgumentException("Invalid Google change notice.");
                if (link.Notice is { } prior && prior.Kind == notice.Kind && prior.ETag == notice.ETag) continue;
                WriteLink(connection, transaction, link with { Revision = checked(link.Revision + 1), Notice = notice, Review = null }); changed++;
            }
            var next = expected with { Revision = checked(expected.Revision + 1), Cursor = batch.Cursor, LastCheckedUtc = (clock ?? TimeProvider.System).GetUtcNow(),
                Failures = 0, RetryAfterUtc = null, Problem = "" };
            WriteFeed(connection, transaction, next);
            return new GoogleFeedResult(next, changed, current.Count(l => l.Pending), batch.Full, batch.Reset);
        });
    public Task<GoogleCalendarFeed> FailFeedAsync(GoogleCalendarFeed expected, bool pause, TimeSpan? providerDelay) => ExecuteAsync(expected.Binding.Scope, (connection, transaction, _) =>
    {
        RequireFeed(connection, transaction, expected);
        var failures = Math.Min(expected.Failures + 1, 20);
        var seconds = Math.Min(900, 30 * Math.Pow(2, Math.Min(failures - 1, 5)));
        if (providerDelay is { } delay) seconds = Math.Max(seconds, Math.Clamp(delay.TotalSeconds, 0, 86400));
        var next = expected with { Revision = checked(expected.Revision + 1), Enabled = expected.Enabled && !pause, Failures = failures,
            RetryAfterUtc = (clock ?? TimeProvider.System).GetUtcNow().AddSeconds(seconds),
            Problem = pause ? "Checks paused after an authorization/access/configuration failure. Reconnect/review access, then explicitly enable again."
                : "Check failed; previous cursor/notices and pending work remain. Automatic retries back off; Scan now overrides the delay." };
        WriteFeed(connection, transaction, next); return next;
    });
    public Task<GoogleCalendarFeed> DisableFeedAsync(GoogleFeedBinding binding) => ExecuteAsync(binding.Scope, (connection, transaction, _) =>
    {
        ValidateBinding(binding);
        var current = ReadFeeds(connection, transaction, binding.Scope).SingleOrDefault(f => f.Binding.Matches(binding)) ?? throw new CalendarConflictException();
        // A read-only scan may have advanced the revision since selection. Opt-out
        // still wins, while preserving its latest cursor rather than restoring an old one.
        var next = current with { Revision = checked(current.Revision + 1), Enabled = false, Failures = 0, RetryAfterUtc = null, Problem = "" };
        WriteFeed(connection, transaction, next); return next;
    });
    public Task<GoogleCalendarFeed> SnoozeEmptyFeedAsync(GoogleCalendarFeed expected) => ExecuteAsync(expected.Binding.Scope, (connection, transaction, _) =>
    {
        RequireFeed(connection, transaction, expected);
        var next = expected with { Revision = checked(expected.Revision + 1), RetryAfterUtc = (clock ?? TimeProvider.System).GetUtcNow().AddMinutes(5),
            Problem = "No linked/conflicted events are ready to check. Pending operations remain unchanged; automatic checks will try later." };
        WriteFeed(connection, transaction, next); return next;
    });
    private static bool Opaque(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 8192 && !value.Any(char.IsControl);
    private static void RequireFeed(SqliteConnection connection, SqliteTransaction transaction, GoogleCalendarFeed expected)
    {
        ValidateBinding(expected.Binding);
        var current = ReadFeeds(connection, transaction, expected.Binding.Scope).SingleOrDefault(f => f.Binding.Matches(expected.Binding));
        if (expected.Revision == 0 ? current is not null : current != expected) throw new CalendarConflictException();
    }
    private static IReadOnlyList<GoogleCalendarFeed> ReadFeeds(SqliteConnection connection, SqliteTransaction transaction, string scope)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Subject,ClientId,CalendarId,Revision,Data FROM GoogleCalendarFeeds WHERE Scope=$scope;"; command.Parameters.AddWithValue("$scope", scope);
        using var reader = command.ExecuteReader(); var rows = new List<GoogleCalendarFeed>();
        while (reader.Read())
        {
            var feed = JsonSerializer.Deserialize<GoogleCalendarFeed>(reader.GetString(4)) ?? throw new InvalidDataException("Unreadable Google feed. Nothing was reset.");
            if (feed.Binding is null) throw new InvalidDataException("Invalid Google feed binding.");
            ValidateBinding(feed.Binding);
            if (feed.Binding.Scope != scope || feed.Binding.Subject != reader.GetString(0) || feed.Binding.ClientId != reader.GetString(1)
                || feed.Binding.CalendarId != reader.GetString(2) || feed.Revision != reader.GetInt64(3) || feed.Revision <= 0 || feed.QueryVersion != 1
                || feed.Cursor is not null && !Opaque(feed.Cursor) || feed.Failures is < 0 or > 20 || feed.Problem is null || feed.Problem.Length > 1024
                || feed.LastCheckedUtc is { Offset: var lastOffset } && lastOffset != TimeSpan.Zero || feed.RetryAfterUtc is { Offset: var retryOffset } && retryOffset != TimeSpan.Zero)
                throw new InvalidDataException("Invalid/newer Google feed data. Nothing was reset.");
            rows.Add(feed);
        }
        return rows.OrderBy(f => f.Binding.CalendarName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Binding.CalendarId).ToArray();
    }
    private static void ValidateBinding(GoogleFeedBinding binding)
    {
        TimeBlockRules.ValidateScope(binding.Scope);
        foreach (var text in new[] { binding.Subject, binding.ClientId, binding.CalendarId, binding.CalendarName })
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 || text.Any(char.IsControl)) throw new InvalidDataException("Invalid Google feed binding.");
    }
    private static void WriteFeed(SqliteConnection connection, SqliteTransaction transaction, GoogleCalendarFeed feed)
    {
        ValidateBinding(feed.Binding);
        if (feed.Revision == 1)
        {
            using var limit = connection.CreateCommand(); limit.Transaction = transaction;
            limit.CommandText = "SELECT count(*) FROM GoogleCalendarFeeds WHERE Scope=$scope;"; limit.Parameters.AddWithValue("$scope", feed.Binding.Scope);
            if ((long)limit.ExecuteScalar()! >= 50) throw new ArgumentException("This Todo scope already has 50 saved calendar monitors.");
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GoogleCalendarFeeds (Scope,Subject,ClientId,CalendarId,Revision,Data) VALUES ($scope,$subject,$client,$calendar,$revision,$data)
            ON CONFLICT(Scope,Subject,ClientId,CalendarId) DO UPDATE SET Revision=$revision,Data=$data;
            """;
        command.Parameters.AddWithValue("$scope", feed.Binding.Scope); command.Parameters.AddWithValue("$subject", feed.Binding.Subject);
        command.Parameters.AddWithValue("$client", feed.Binding.ClientId); command.Parameters.AddWithValue("$calendar", feed.Binding.CalendarId);
        command.Parameters.AddWithValue("$revision", feed.Revision); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(feed)); command.ExecuteNonQuery();
    }
}
