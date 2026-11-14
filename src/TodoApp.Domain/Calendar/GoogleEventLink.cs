namespace TodoApp.Domain.Calendar;

public enum GoogleLinkState { PendingCreate, Linked, PendingUpdate, PendingDelete, Deleted, Conflict, Detached }

public enum GoogleReviewKind { Present, Missing, Unsafe }
public sealed record GoogleReviewedFields(string ETag, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public sealed record GoogleEventReview(Guid Id, DateTimeOffset ObservedUtc, GoogleReviewKind Kind,
    TimeBlock? Local, GoogleReviewedFields? Remote, string Problem)
{
    public string Details => $"Reviewed {ObservedUtc:u}\nLocal block: {(Local is null ? "Missing (will not be recreated)" : $"{Local.Title}\n{Local.StartUtc:u} to {Local.EndUtc:u}")}\nGoogle event: " +
        (Remote is null ? Kind.ToString() : $"{Remote.Title}\n{Remote.StartUtc:u} to {Remote.EndUtc:u}") + $"\n{Problem}";
}

public sealed record CalendarBlockSnapshot(Guid BlockId, long BlockRevision, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static CalendarBlockSnapshot From(TimeBlock block) => new(block.Id, block.Revision, block.Title, block.StartUtc, block.EndUtc);
}

public sealed record GoogleEventLink(Guid LinkId, string Scope, Guid BlockId, long Revision, string Subject, string ClientId,
    string CalendarId, string CalendarName, string Proof, string EventId, GoogleLinkState State,
    CalendarBlockSnapshot Desired, CalendarBlockSnapshot? Published, string ETag, string Problem, GoogleEventReview? Review = null, GoogleChangeNotice? Notice = null)
{
    public bool Pending => State is GoogleLinkState.PendingCreate or GoogleLinkState.PendingUpdate or GoogleLinkState.PendingDelete;
    public string Label => $"{Desired.Title} · {State} · {CalendarName}" + (Notice is null ? "" : " · Google change detected");
    public string Details => $"{State}\nCalendar: {CalendarName}\nCaptured start: {Desired.StartUtc:u}\nCaptured end: {Desired.EndUtc:u}\nLast acknowledged local revision: {Published?.BlockRevision.ToString() ?? "None"}\nCaptured operation revision: {Desired.BlockRevision}\n{Problem}\n{Notice?.Message}";
    public override string ToString() => "Google event link";
}
