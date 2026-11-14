using System.Net;
using TodoApp.Domain.Calendar;

namespace TodoApp.Maui.Models;

public sealed record GoogleCalendarChoice(string Id, string Name)
{
    public string Label => $"{Name} ({Id})";
}
public sealed record GoogleEventResource(string Id, string ETag, string Proof, string LinkId, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public bool Matches(CalendarBlockSnapshot snapshot) => Title == snapshot.Title && StartUtc == snapshot.StartUtc && EndUtc == snapshot.EndUtc;
}
public sealed class GoogleCalendarHttpException(HttpStatusCode status) : Exception($"Google Calendar returned {(int)status}. Pending work was retained; refresh/review before retrying.")
{
    public HttpStatusCode Status { get; } = status;
}
public sealed class GoogleEventConflictException(string message) : Exception(message);
