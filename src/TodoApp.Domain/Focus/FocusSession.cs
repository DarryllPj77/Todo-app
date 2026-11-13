using TodoApp.Domain.Recurrence;
using System.Text.Json.Serialization;

namespace TodoApp.Domain.Focus;

public enum FocusKind { Focus, ShortBreak, LongBreak }
public enum FocusStatus { Running, Paused, Completed, Stopped, Interrupted }
public enum FocusEndReason { TargetReached, UserStopped, AppRestarted, AppSuspended, AccountChanged, TaskUnavailable, OccurrenceChanged, TimingGap, StorageFailure }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FocusPreferences([property: JsonRequired] int FocusMinutes = 25,
    [property: JsonRequired] int ShortBreakMinutes = 5, [property: JsonRequired] int LongBreakMinutes = 15)
{
    public FocusPreferences Validate()
    {
        if (FocusMinutes is < 1 or > 180 || ShortBreakMinutes is < 1 or > 60 || LongBreakMinutes is < 1 or > 60)
            throw new ArgumentException("Use 1-180 focus minutes and 1-60 minutes for each break.");
        return this;
    }
    public TimeSpan Duration(FocusKind kind) => TimeSpan.FromMinutes(kind switch
    {
        FocusKind.Focus => Validate().FocusMinutes,
        FocusKind.ShortBreak => Validate().ShortBreakMinutes,
        FocusKind.LongBreak => Validate().LongBreakMinutes,
        _ => throw new ArgumentException("Choose a valid session kind.")
    });
}

public sealed record FocusTask(string Scope, Guid Id, string Title, RepeatState Recurrence);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FocusSession([property: JsonRequired] Guid Id, [property: JsonRequired] string Scope,
    [property: JsonRequired] Guid? TaskId, [property: JsonRequired] string Title, [property: JsonRequired] RepeatState? TaskRecurrence,
    [property: JsonRequired] FocusKind Kind, [property: JsonRequired] long TargetTicks, [property: JsonRequired] long ElapsedTicks,
    [property: JsonRequired] FocusStatus Status, [property: JsonRequired] DateTimeOffset StartedAtUtc,
    [property: JsonRequired] DateTimeOffset UpdatedAtUtc, DateTimeOffset? EndedAtUtc = null, FocusEndReason? EndReason = null)
{
    [JsonIgnore] public bool IsActive => Status is FocusStatus.Running or FocusStatus.Paused;
    [JsonIgnore] public TimeSpan Elapsed => TimeSpan.FromTicks(ElapsedTicks);
    [JsonIgnore] public TimeSpan Remaining => TimeSpan.FromTicks(TargetTicks - ElapsedTicks);
}
public sealed record FocusEntry(long Sequence, long Revision, FocusSession Session);
public sealed record FocusSnapshot(FocusPreferences Preferences, IReadOnlyList<FocusEntry> History,
    FocusEntry? Active, bool OtherScopeActive, bool CanControl);

public static class FocusRules
{
    public static readonly TimeSpan MaximumHeartbeatGap = TimeSpan.FromSeconds(10);
    public static FocusSession Start(string scope, FocusTask? task, FocusKind kind, FocusPreferences preferences, DateTimeOffset now)
    {
        var duration = preferences.Duration(kind);
        if (kind == FocusKind.Focus && (task is null || task.Scope != scope)) throw new ArgumentException("Choose a pending task in this workspace.");
        var utc = now.ToUniversalTime();
        var result = new FocusSession(Guid.NewGuid(), scope, kind == FocusKind.Focus ? task!.Id : null,
            kind == FocusKind.Focus ? task!.Title : kind == FocusKind.ShortBreak ? "Short break" : "Long break",
            kind == FocusKind.Focus ? task!.Recurrence : null, kind, duration.Ticks, 0, FocusStatus.Running, utc, utc);
        Validate(result); return result;
    }
    public static void Validate(FocusSession session)
    {
        if (session.Id == Guid.Empty || string.IsNullOrWhiteSpace(session.Scope) || session.Scope.Length > 2048 ||
            string.IsNullOrWhiteSpace(session.Title) || session.Title.Length > 200 || !Enum.IsDefined(session.Kind) || !Enum.IsDefined(session.Status) ||
            (session.EndReason is { } reason && !Enum.IsDefined(reason))) throw new InvalidDataException("Invalid focus history. Nothing was reset.");
        var maximum = TimeSpan.FromMinutes(session.Kind == FocusKind.Focus ? 180 : 60).Ticks;
        if (session.TargetTicks < TimeSpan.TicksPerMinute || session.TargetTicks > maximum || session.ElapsedTicks < 0 || session.ElapsedTicks > session.TargetTicks ||
            (session.Status == FocusStatus.Completed) != (session.ElapsedTicks == session.TargetTicks) ||
            session.StartedAtUtc.Offset != TimeSpan.Zero || session.UpdatedAtUtc.Offset != TimeSpan.Zero || session.UpdatedAtUtc < session.StartedAtUtc ||
            (session.IsActive && (session.EndedAtUtc is not null || session.EndReason is not null)) ||
            (!session.IsActive && (session.EndedAtUtc != session.UpdatedAtUtc || session.EndReason is null)) ||
            (session.Status == FocusStatus.Completed && session.EndReason != FocusEndReason.TargetReached) ||
            (session.Kind == FocusKind.Focus && (session.TaskId is null || session.TaskId == Guid.Empty || session.TaskRecurrence is null)) ||
            (session.Kind != FocusKind.Focus && (session.TaskId is not null || session.TaskRecurrence is not null)))
            throw new InvalidDataException("Invalid focus duration or lifecycle. Nothing was reset.");
        if (session.TaskRecurrence is { } recurrence) RecurrenceRules.Validate(recurrence);
    }
    private static DateTimeOffset Stamp(FocusSession session, DateTimeOffset now) => now.ToUniversalTime() < session.UpdatedAtUtc ? session.UpdatedAtUtc : now.ToUniversalTime();
    public static FocusSession Advance(FocusSession session, TimeSpan elapsed, DateTimeOffset now)
    {
        Validate(session);
        if (session.Status != FocusStatus.Running) return session;
        if (elapsed < TimeSpan.Zero || elapsed > MaximumHeartbeatGap) return Finish(session, FocusEndReason.TimingGap, now);
        var ticks = session.ElapsedTicks + Math.Min(elapsed.Ticks, session.TargetTicks - session.ElapsedTicks);
        var updated = Stamp(session, now);
        return ticks == session.TargetTicks
            ? session with { ElapsedTicks = ticks, UpdatedAtUtc = updated, EndedAtUtc = updated, EndReason = FocusEndReason.TargetReached, Status = FocusStatus.Completed }
            : session with { ElapsedTicks = ticks, UpdatedAtUtc = updated };
    }
    public static FocusSession Pause(FocusSession session, DateTimeOffset now) => session.Status == FocusStatus.Running
        ? session with { Status = FocusStatus.Paused, UpdatedAtUtc = Stamp(session, now) } : session;
    public static FocusSession Resume(FocusSession session, DateTimeOffset now) => session.Status == FocusStatus.Paused
        ? session with { Status = FocusStatus.Running, UpdatedAtUtc = Stamp(session, now) } : session;
    public static FocusSession Finish(FocusSession session, FocusEndReason reason, DateTimeOffset now)
    {
        if (!session.IsActive) return session;
        if (reason == FocusEndReason.TargetReached || !Enum.IsDefined(reason)) throw new ArgumentException("Choose a valid stop reason.");
        var stamp = Stamp(session, now);
        return session with { Status = reason == FocusEndReason.UserStopped ? FocusStatus.Stopped : FocusStatus.Interrupted,
            EndReason = reason, UpdatedAtUtc = stamp, EndedAtUtc = stamp };
    }
    public static long TimeSpentTicks(IEnumerable<FocusEntry> entries, Guid? taskId = null) => entries
        .Where(e => e.Session.Kind == FocusKind.Focus && (taskId is null || e.Session.TaskId == taskId))
        .Aggregate(0L, (sum, entry) => checked(sum + entry.Session.ElapsedTicks));
}
