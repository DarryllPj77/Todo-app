using System.Text.Json.Serialization;

namespace TodoApp.Domain.Recurrence;

[JsonConverter(typeof(RepeatFrequencyJsonConverter))]
public enum RepeatFrequency { Daily = 1, Weekly = 2, Monthly = 3 }
public sealed class RepeatFrequencyJsonConverter() : JsonStringEnumConverter<RepeatFrequency>(namingPolicy: null, allowIntegerValues: false);
public sealed class TaskRecurrenceException(string message) : ArgumentException(message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepeatRule([property: JsonRequired] RepeatFrequency Frequency,
    [property: JsonRequired] int Interval, [property: JsonRequired] DateOnly AnchorDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepeatState([property: JsonRequired] RepeatRule? Rule,
    [property: JsonRequired] long Occurrence, [property: JsonRequired] long RuleStart)
{
    public static RepeatState None { get; } = new(null, 0, 0);
    [JsonIgnore] public bool IsActive => Rule is not null;
}

public static class RecurrenceRules
{
    public const long MaxOccurrence = 1_000_000;
    public static void ValidateRule(RepeatRule? rule)
    {
        if (rule is not null && (!Enum.IsDefined(rule.Frequency) || rule.Interval is < 1 or > 365))
            throw new TaskRecurrenceException("Choose Daily, Weekly, or Monthly with an interval from 1 to 365.");
    }
    public static void Validate(RepeatState state)
    {
        if (state.Occurrence is < 0 or > MaxOccurrence || state.RuleStart < 0 || state.RuleStart > state.Occurrence ||
            (state.Rule is null && state.RuleStart != state.Occurrence))
            throw new TaskRecurrenceException("The recurrence counter is invalid. Refresh and review the task.");
        ValidateRule(state.Rule);
        if (state.IsActive) _ = DueDate(state);
    }
    public static DateOnly DueDate(RepeatState state)
    {
        var rule = state.Rule ?? throw new TaskRecurrenceException("This task has no active repeat rule.");
        ValidateRule(rule);
        try
        {
            var distance = checked((state.Occurrence - state.RuleStart) * rule.Interval);
            return rule.Frequency switch
            {
                RepeatFrequency.Daily => rule.AnchorDate.AddDays(checked((int)distance)),
                RepeatFrequency.Weekly => rule.AnchorDate.AddDays(checked((int)(distance * 7))),
                // Always calculate from the original anchor, avoiding Jan31 -> Feb28 -> Mar28 drift.
                RepeatFrequency.Monthly => rule.AnchorDate.AddMonths(checked((int)distance)),
                _ => throw new TaskRecurrenceException("Choose a valid repeat frequency.")
            };
        }
        catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
        { throw new TaskRecurrenceException("The next recurrence date is outside the supported calendar. Stop or change the repeat rule."); }
    }
    public static RepeatState Configure(RepeatState current, RepeatRule? rule)
    {
        Validate(current); ValidateRule(rule);
        if (current.Rule == rule) return current; // Reapplying a rule never resets an advanced occurrence.
        var next = new RepeatState(rule, current.Occurrence, current.Occurrence); Validate(next); return next;
    }
    public static RepeatState Complete(RepeatState current, RepeatState expected)
    {
        Validate(current); Validate(expected);
        if (expected.Occurrence < current.Occurrence) return current; // Already advanced, including after disable/reconfigure.
        if (expected != current) throw new TaskRecurrenceException("This occurrence or rule changed. Refresh before completing it.");
        if (!current.IsActive) throw new TaskRecurrenceException("This task has no active repeat rule.");
        var next = current with { Occurrence = checked(current.Occurrence + 1) }; Validate(next); return next;
    }
    public static void ValidateWrite(RepeatState current, RepeatState candidate, DateOnly? dueDate, bool wasCompleted, bool completed)
    {
        Validate(current); Validate(candidate);
        var unchanged = candidate == current;
        var configured = candidate.Occurrence == current.Occurrence && candidate.RuleStart == current.Occurrence;
        var advanced = current.IsActive && candidate.Rule == current.Rule && candidate.RuleStart == current.RuleStart && candidate.Occurrence == current.Occurrence + 1;
        if (!unchanged && !configured && !advanced)
            throw new TaskRecurrenceException("Recurrence must preserve its counter or advance exactly one occurrence. Refresh and review conflicts.");
        if (candidate.IsActive && (completed || (wasCompleted && !unchanged)))
            throw new TaskRecurrenceException("Reopen a completed task before enabling recurrence. Use Recurring tasks to complete an active occurrence.");
        if (candidate.IsActive && dueDate != DueDate(candidate))
            throw new TaskRecurrenceException("The due date must match the current occurrence. Change the repeat rule in Recurring tasks.");
    }
    public static string Describe(RepeatState? state) => state?.Rule is { } rule
        ? $"Every {rule.Interval} {rule.Frequency}; due {DueDate(state):yyyy-MM-dd}; occurrence {state.Occurrence + 1}"
        : $"Not repeating; {state?.Occurrence ?? 0} occurrences advanced";
}
