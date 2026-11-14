namespace TodoApp.Domain.Calendar;

public enum GoogleLinkState { PendingCreate, Linked, PendingUpdate, PendingDelete, Deleted, Conflict }

public sealed record CalendarBlockSnapshot(Guid BlockId, long BlockRevision, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static CalendarBlockSnapshot From(TimeBlock block) => new(block.Id, block.Revision, block.Title, block.StartUtc, block.EndUtc);
}

public sealed record GoogleEventLink(Guid LinkId, string Scope, Guid BlockId, long Revision, string Subject, string ClientId,
    string CalendarId, string CalendarName, string Proof, string EventId, GoogleLinkState State,
    CalendarBlockSnapshot Desired, CalendarBlockSnapshot? Published, string ETag, string Problem)
{
    public bool Pending => State is GoogleLinkState.PendingCreate or GoogleLinkState.PendingUpdate or GoogleLinkState.PendingDelete;
    public string Label => $"{Desired.Title} · {State} · {CalendarName}";
    public string Details => $"{State}\nCalendar: {CalendarName}\nCaptured start: {Desired.StartUtc:u}\nCaptured end: {Desired.EndUtc:u}\nLast published local revision: {Published?.BlockRevision.ToString() ?? "None"}\nCaptured operation revision: {Desired.BlockRevision}\n{Problem}";
    public override string ToString() => "Google event link";
}
