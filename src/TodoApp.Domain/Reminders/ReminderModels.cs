namespace TodoApp.Domain.Reminders;

public sealed record ReminderTask(string Scope, Guid TaskId, string Title, DateOnly? DueDate, bool Completed)
{
    public string Key => Scope + "|" + TaskId.ToString("N");
    public string Label => (Scope == "device" ? "Device: " : "Synced: ") + Title;
}

public sealed class ReminderRecord
{
    public int Id { get; set; }
    public string Scope { get; set; } = "device";
    public Guid TaskId { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? TaskDueDate { get; set; }
    public DateTimeOffset FireAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "";
    public DateTime WallTime { get; set; }
    public bool Enabled { get; set; }
    public bool Attempted { get; set; }
    public string Status { get; set; } = "Not scheduled";
    public string Key => Scope + "|" + TaskId.ToString("N");
    public string Summary => $"{Title} | {WallTime:g} ({TimeZoneId}) | {Status}";
}

public static class ReminderTime
{
    // Resolve once to an absolute UTC instant. Travel does not move the alert.
    // Ambiguous/invalid DST times require another choice instead of guessing.
    public static DateTimeOffset Resolve(DateTime date, TimeSpan time, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) throw new ArgumentException("Choose a time within the day.");
        var wall = DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall) || zone.IsAmbiguousTime(wall))
            throw new ArgumentException("This time is skipped or repeated by daylight saving. Choose an unambiguous time.");
        var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone));
        if (utc < now.AddMinutes(1) || utc > now.AddDays(365))
            throw new ArgumentException("Choose a reminder at least one minute ahead and within 365 days.");
        return utc;
    }
}
