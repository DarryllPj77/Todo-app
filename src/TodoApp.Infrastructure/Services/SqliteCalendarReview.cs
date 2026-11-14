using TodoApp.Domain.Calendar;
using Microsoft.Data.Sqlite;

namespace TodoApp.Infrastructure.Services;

public sealed partial class SqliteCalendarStore
{
    public Task<GoogleEventLink> SaveReviewAsync(GoogleEventLink expected, TimeBlock? local, GoogleReviewKind kind, GoogleReviewedFields? remote, string problem) =>
        ExecuteAsync(expected.Scope, (connection, transaction, _) =>
        {
            var current = RequireLink(connection, transaction, expected);
            if (current.State is not (GoogleLinkState.Linked or GoogleLinkState.Conflict))
                throw new ArgumentException("Retry pending operations before reviewing. Closed links cannot be reconciled.");
            var actual = Read(connection, transaction, expected.Scope).SingleOrDefault(b => b.Id == expected.BlockId);
            if (actual != local) throw new CalendarConflictException();
            var review = new GoogleEventReview(Guid.NewGuid(), (clock ?? TimeProvider.System).GetUtcNow(), kind, actual, remote, problem);
            if (!ValidReview(review, current)) throw new ArgumentException("The Google event cannot be represented by a safe review.");
            var next = current with { Revision = checked(current.Revision + 1), Review = review };
            WriteLink(connection, transaction, next); return next;
        });

    public Task<GoogleEventLink> StageReviewedLocalAsync(GoogleEventLink expected) => ExecuteAsync(expected.Scope, (connection, transaction, _) =>
    {
        var current = RequireReview(connection, transaction, expected);
        var review = current.Review!;
        if (review.Kind != GoogleReviewKind.Present || review.Remote is null || review.Local is null)
            throw new ArgumentException("A live Google event and an unchanged local block are required to keep local fields.");
        var next = current with { Revision = checked(current.Revision + 1), State = GoogleLinkState.PendingUpdate,
            Desired = CalendarBlockSnapshot.From(review.Local), ETag = review.Remote.ETag, Review = null, Problem = "" };
        WriteLink(connection, transaction, next); return next;
    });

    public Task<GoogleEventLink> AdoptReviewedGoogleAsync(GoogleEventLink expected) => ExecuteAsync(expected.Scope, (connection, transaction, _) =>
    {
        var current = RequireReview(connection, transaction, expected); var review = current.Review!;
        if (review.Kind != GoogleReviewKind.Present || review.Remote is null) throw new ArgumentException("Review a live Google event before adopting it.");
        var remote = review.Remote;
        if (string.IsNullOrWhiteSpace(remote.Title) || remote.Title.Length > 200 || remote.Title != remote.Title.Trim() || remote.Title.Any(char.IsControl)
            || review.Local is null && remote.StartUtc.Year is < 2000 or > 2099 || remote.StartUtc.Ticks % TimeSpan.TicksPerMinute != 0
            || (remote.EndUtc - remote.StartUtc).Ticks % TimeSpan.TicksPerMinute != 0 || remote.EndUtc - remote.StartUtc < TimeSpan.FromMinutes(1)
            || remote.EndUtc - remote.StartUtc > TimeSpan.FromDays(1))
            throw new ArgumentException("Google fields do not fit a local block: use a trimmed 1-200 character title, whole-minute start/duration, and 1-1440 minutes. Keep local fields or edit Google and review again.");
        CalendarBlockSnapshot baseline;
        if (review.Local is { } original)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(original.TimeZoneId); }
            catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            { throw new ArgumentException("The block's captured time zone is unavailable. Keep local fields or review on a compatible device."); }
            var wall = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(remote.StartUtc, zone).DateTime, DateTimeKind.Unspecified);
            if (wall.Year is < 2000 or > 2099 || wall.Ticks % TimeSpan.TicksPerMinute != 0) throw new ArgumentException("Google's start does not fit this block's local date range/minute precision.");
            var adopted = original with { Title = remote.Title, WallStart = wall, StartUtc = remote.StartUtc, EndUtc = remote.EndUtc,
                Revision = checked(original.Revision + 1), UpdatedUtc = (clock ?? TimeProvider.System).GetUtcNow() };
            if (Read(connection, transaction, expected.Scope).Any(other => TimeBlockRules.Overlaps(adopted, other)))
                throw new ArgumentException("Google's time overlaps another local block. No local block or link was changed.");
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE CalendarBlocks SET Revision=$revision,Data=$data WHERE Scope=$scope AND Id=$id;";
            command.Parameters.AddWithValue("$revision", adopted.Revision); command.Parameters.AddWithValue("$data", System.Text.Json.JsonSerializer.Serialize(adopted));
            command.Parameters.AddWithValue("$scope", adopted.Scope); command.Parameters.AddWithValue("$id", adopted.Id.ToString("N")); command.ExecuteNonQuery();
            baseline = CalendarBlockSnapshot.From(adopted);
        }
        else baseline = new(current.BlockId, current.Desired.BlockRevision, remote.Title, remote.StartUtc, remote.EndUtc);
        var next = current with { Revision = checked(current.Revision + 1), State = GoogleLinkState.Linked,
            Desired = baseline, Published = baseline, ETag = remote.ETag, Review = null, Problem = "" };
        WriteLink(connection, transaction, next); return next;
    });

    public Task<GoogleEventLink> AcceptReviewedMissingAsync(GoogleEventLink expected) => ExecuteAsync(expected.Scope, (connection, transaction, _) =>
    {
        var current = RequireReview(connection, transaction, expected);
        if (current.Review!.Kind != GoogleReviewKind.Missing) throw new ArgumentException("Review the missing Google event before accepting deletion.");
        var next = current with { Revision = checked(current.Revision + 1), State = GoogleLinkState.Deleted, Review = null,
            Problem = "Reviewed Google deletion accepted. Local block/task retained; a new publish needs explicit confirmation." };
        WriteLink(connection, transaction, next); return next;
    });

    public Task<GoogleEventLink> DetachLinkAsync(GoogleEventLink expected) => ExecuteAsync(expected.Scope, (connection, transaction, _) =>
    {
        var current = RequireLink(connection, transaction, expected);
        if (current.State is not (GoogleLinkState.Linked or GoogleLinkState.Conflict)) throw new ArgumentException("Retry pending work before detaching; closed links are already inactive.");
        var next = current with { Revision = checked(current.Revision + 1), State = GoogleLinkState.Detached, Review = null,
            Problem = "Detached locally only. The Google event may remain; remove it separately if needed. A new publish may create another event." };
        WriteLink(connection, transaction, next); return next;
    });

    private static GoogleEventLink RequireLink(SqliteConnection connection, SqliteTransaction transaction, GoogleEventLink expected)
    {
        var current = ReadLinks(connection, transaction, expected.Scope).SingleOrDefault(l => l.BlockId == expected.BlockId);
        if (current != expected) throw new CalendarConflictException(); return current;
    }
    private static GoogleEventLink RequireReview(SqliteConnection connection, SqliteTransaction transaction, GoogleEventLink expected)
    {
        var current = RequireLink(connection, transaction, expected);
        if (current.Review is null || current.State is not (GoogleLinkState.Linked or GoogleLinkState.Conflict)) throw new ArgumentException("Load a current Google/local review before choosing a resolution.");
        var actual = Read(connection, transaction, expected.Scope).SingleOrDefault(b => b.Id == current.BlockId);
        if (actual != current.Review.Local) throw new CalendarConflictException();
        return current;
    }
}
