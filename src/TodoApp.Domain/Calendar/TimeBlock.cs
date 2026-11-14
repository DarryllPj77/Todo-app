namespace TodoApp.Domain.Calendar;

public sealed record TimeBlockDraft(Guid TaskId, string Title, DateTime WallStart, string TimeZoneId, int DurationMinutes);

public sealed record TimeBlock(Guid Id, string Scope, Guid TaskId, string Title, DateTime WallStart,
    string TimeZoneId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, long Revision, DateTimeOffset UpdatedUtc)
{
    public string Summary => $"{WallStart:g} ({TimeZoneId}) · {(EndUtc - StartUtc).TotalMinutes:0} minutes";
}

public static class TimeBlockRules
{
    public static TimeBlock Create(Guid id, string scope, TimeBlockDraft draft, long revision, DateTimeOffset updated)
    {
        if (id == Guid.Empty || draft.TaskId == Guid.Empty || revision is < 1 or > int.MaxValue)
            throw new ArgumentException("Choose a valid time block and task.");
        ValidateScope(scope);
        var title = draft.Title?.Trim() ?? "";
        if (title.Length is < 1 or > 200 || title.Any(char.IsControl))
            throw new ArgumentException("Use a task title of 1-200 characters without control characters.");
        if (draft.DurationMinutes is < 1 or > 1440) throw new ArgumentException("Use a duration of 1-1440 whole minutes.");
        var wall = DateTime.SpecifyKind(draft.WallStart, DateTimeKind.Unspecified);
        if (wall.Year is < 2000 or > 2099 || wall.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Choose a start between 2000 and 2099, in whole minutes.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ArgumentException("This time zone is unavailable. Review the block on a compatible device."); }
        if (zone.IsInvalidTime(wall) || zone.IsAmbiguousTime(wall))
            throw new ArgumentException("This local time is skipped or repeated by daylight saving. Choose an unambiguous start.");
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone));
        return new(id, scope, draft.TaskId, title, wall, zone.Id, start, start.AddMinutes(draft.DurationMinutes), revision, updated.ToUniversalTime());
    }

    public static void ValidateScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 2048 || scope.Any(char.IsControl))
            throw new ArgumentException("Choose a valid calendar workspace.");
    }

    public static bool Overlaps(TimeBlock first, TimeBlock second) => first.Scope == second.Scope
        && first.Id != second.Id && first.StartUtc < second.EndUtc && second.StartUtc < first.EndUtc;
}
