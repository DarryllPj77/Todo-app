using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;

namespace TodoApp.Infrastructure.Services;

public sealed partial class SqliteCalendarStore
{
    public Task<IReadOnlyList<GoogleEventLink>> LinksAsync(string scope) => ExecuteAsync(scope, ReadLinks);

    public Task<GoogleEventLink> StagePublishAsync(string scope, Guid blockId, long blockRevision, long? linkRevision,
        string subject, string clientId, string calendarId, string calendarName) => ExecuteAsync(scope, (connection, transaction, _) =>
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(calendarId) || calendarId.Length > 1024 || calendarId.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(calendarName) || calendarName.Length > 1024)
            throw new ArgumentException("Choose a verified Google account and owned calendar.");
        var block = Read(connection, transaction, scope).SingleOrDefault(b => b.Id == blockId && b.Revision == blockRevision)
            ?? throw new CalendarConflictException();
        var existing = ReadLinks(connection, transaction, scope).SingleOrDefault(l => l.BlockId == blockId);
        if (existing?.Revision != linkRevision) throw new CalendarConflictException();
        GoogleEventLink link;
        if (existing is null || existing.State is GoogleLinkState.Deleted or GoogleLinkState.Detached)
        {
            var id = Guid.NewGuid();
            link = new(id, scope, blockId, (existing?.Revision ?? 0) + 1, subject, clientId, calendarId, calendarName,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), "todo" + id.ToString("N"),
                GoogleLinkState.PendingCreate, CalendarBlockSnapshot.From(block), null, "", "");
        }
        else
        {
            if (existing.State != GoogleLinkState.Linked) throw new ArgumentException("Retry/review the existing operation before publishing another version.");
            if (existing.Subject != subject || existing.ClientId != clientId || existing.CalendarId != calendarId)
                throw new ArgumentException("This block is linked to another Google account/client/calendar. Delete that linked event before changing its destination.");
            link = existing with { Revision = existing.Revision + 1, Desired = CalendarBlockSnapshot.From(block), State = GoogleLinkState.PendingUpdate, Problem = "", Review = null, Notice = null };
        }
        WriteLink(connection, transaction, link); return link;
    });

    public Task<GoogleEventLink> StageDeleteAsync(string scope, Guid blockId, long expectedRevision) => ExecuteAsync(scope, (connection, transaction, _) =>
    {
        var current = ReadLinks(connection, transaction, scope).SingleOrDefault(l => l.BlockId == blockId && l.Revision == expectedRevision)
            ?? throw new CalendarConflictException();
        if (current.State != GoogleLinkState.Linked) throw new ArgumentException("Only a reviewed, linked event can be deleted. Retry pending work or review the conflict first.");
        var link = current with { Revision = current.Revision + 1, State = GoogleLinkState.PendingDelete, Problem = "", Review = null, Notice = null };
        WriteLink(connection, transaction, link); return link;
    });

    public Task<GoogleEventLink> FinishLinkAsync(GoogleEventLink expected, GoogleLinkState state, string etag, string problem = "") => ExecuteAsync(expected.Scope, (connection, transaction, _) =>
    {
        var current = ReadLinks(connection, transaction, expected.Scope).SingleOrDefault(l => l.BlockId == expected.BlockId);
        if (current != expected) throw new CalendarConflictException();
        if (!expected.Pending || state is not (GoogleLinkState.Linked or GoogleLinkState.Deleted or GoogleLinkState.Conflict)
            || state == GoogleLinkState.Deleted && expected.State != GoogleLinkState.PendingDelete
            || state == GoogleLinkState.Linked && expected.State == GoogleLinkState.PendingDelete
            || state == GoogleLinkState.Linked && string.IsNullOrWhiteSpace(etag)) throw new ArgumentException("Invalid calendar operation transition.");
        var next = current with { Revision = checked(current.Revision + 1), State = state, ETag = etag,
            Published = state == GoogleLinkState.Linked ? current.Desired : current.Published, Problem = problem, Review = null, Notice = null };
        WriteLink(connection, transaction, next); return next;
    });

    private static IReadOnlyList<GoogleEventLink> ReadLinks(SqliteConnection connection, SqliteTransaction transaction, string scope)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT BlockId,Revision,Data FROM GoogleEventLinks WHERE Scope=$scope;"; command.Parameters.AddWithValue("$scope", scope);
        using var reader = command.ExecuteReader(); var result = new List<GoogleEventLink>();
        while (reader.Read())
        {
            var link = JsonSerializer.Deserialize<GoogleEventLink>(reader.GetString(2)) ?? throw new InvalidDataException("Unreadable Google link; nothing was reset.");
            if (link.Scope != scope || link.LinkId == Guid.Empty || link.BlockId == Guid.Empty || link.BlockId.ToString("N") != reader.GetString(0)
                || link.Revision != reader.GetInt64(1) || link.Revision <= 0 || !Enum.IsDefined(link.State) || link.EventId != "todo" + link.LinkId.ToString("N")
                || string.IsNullOrWhiteSpace(link.Subject) || string.IsNullOrWhiteSpace(link.ClientId) || string.IsNullOrWhiteSpace(link.CalendarId)
                || link.CalendarId.Length > 1024 || link.Proof is null || link.Proof.Length != 64 || !link.Proof.All(char.IsAsciiHexDigit)
                || link.Desired is null || !ValidSnapshot(link.Desired, link.BlockId) || link.Published is not null && !ValidSnapshot(link.Published, link.BlockId)
                || link.ETag is null || link.ETag.Length > 1024 || link.ETag.Any(char.IsControl)
                || link.State is GoogleLinkState.Linked or GoogleLinkState.PendingDelete && (link.Published is null || link.ETag.Length == 0)
                || link.State == GoogleLinkState.PendingUpdate && link.ETag.Length == 0
                || link.Review is { } review && !ValidReview(review, link)
                || link.Notice is { } notice && (!Enum.IsDefined(notice.Kind) || notice.DetectedUtc.Offset != TimeSpan.Zero
                    || notice.ETag is null || notice.ETag.Length > 1024 || notice.ETag.Any(char.IsControl) || notice.Message is null || notice.Message.Length > 1024))
                throw new InvalidDataException("Invalid Google link; nothing was reset.");
            result.Add(link);
        }
        return result.OrderBy(l => l.Desired.StartUtc).ThenBy(l => l.BlockId).ToArray();
    }
    private static bool ValidSnapshot(CalendarBlockSnapshot snapshot, Guid blockId) => snapshot.BlockId == blockId && snapshot.BlockRevision > 0
        && !string.IsNullOrWhiteSpace(snapshot.Title) && snapshot.Title.Length <= 200 && !snapshot.Title.Any(char.IsControl)
        && snapshot.StartUtc.Offset == TimeSpan.Zero && snapshot.EndUtc.Offset == TimeSpan.Zero && snapshot.StartUtc < snapshot.EndUtc
        && snapshot.EndUtc - snapshot.StartUtc <= TimeSpan.FromDays(1);
    private static bool ValidReview(GoogleEventReview review, GoogleEventLink link) => review.Id != Guid.Empty && review.ObservedUtc.Offset == TimeSpan.Zero
        && Enum.IsDefined(review.Kind) && link.State is GoogleLinkState.Linked or GoogleLinkState.Conflict
        && (review.Local is null || review.Local.Scope == link.Scope && review.Local.Id == link.BlockId && review.Local.Revision > 0)
        && (review.Kind == GoogleReviewKind.Present ? review.Remote is { } remote && !string.IsNullOrWhiteSpace(remote.ETag)
            && remote.ETag.Length <= 1024 && !remote.ETag.Any(char.IsControl) && remote.Title is not null && remote.Title.Length <= 4096
            && remote.StartUtc.Offset == TimeSpan.Zero && remote.EndUtc.Offset == TimeSpan.Zero && remote.StartUtc < remote.EndUtc : review.Remote is null);
    private static void WriteLink(SqliteConnection connection, SqliteTransaction transaction, GoogleEventLink link)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GoogleEventLinks (Scope,BlockId,Revision,Data) VALUES ($scope,$id,$revision,$data)
            ON CONFLICT(Scope,BlockId) DO UPDATE SET Revision=$revision,Data=$data;
            """;
        command.Parameters.AddWithValue("$scope", link.Scope); command.Parameters.AddWithValue("$id", link.BlockId.ToString("N"));
        command.Parameters.AddWithValue("$revision", link.Revision); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(link)); command.ExecuteNonQuery();
    }
}
