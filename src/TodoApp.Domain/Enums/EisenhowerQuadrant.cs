using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoApp.Domain.Enums;

[JsonConverter(typeof(EisenhowerQuadrantJsonConverter))]
public enum EisenhowerQuadrant
{
    Unclassified = 0,
    UrgentImportant = 1,
    ImportantNotUrgent = 2,
    UrgentNotImportant = 3,
    Neither = 4
}

public sealed class EisenhowerQuadrantJsonConverter() : JsonStringEnumConverter<EisenhowerQuadrant>(namingPolicy: null, allowIntegerValues: false);

public static class Eisenhower
{
    public static EisenhowerQuadrant FromFlags(bool urgent, bool important) => (urgent, important) switch
    {
        (true, true) => EisenhowerQuadrant.UrgentImportant,
        (false, true) => EisenhowerQuadrant.ImportantNotUrgent,
        (true, false) => EisenhowerQuadrant.UrgentNotImportant,
        _ => EisenhowerQuadrant.Neither
    };
    public static bool? IsUrgent(EisenhowerQuadrant quadrant) => quadrant switch
    {
        EisenhowerQuadrant.Unclassified => null,
        EisenhowerQuadrant.UrgentImportant or EisenhowerQuadrant.UrgentNotImportant => true,
        EisenhowerQuadrant.ImportantNotUrgent or EisenhowerQuadrant.Neither => false,
        _ => throw new ArgumentException("Choose a valid urgency/importance quadrant.")
    };
    public static bool? IsImportant(EisenhowerQuadrant quadrant) => quadrant switch
    {
        EisenhowerQuadrant.Unclassified => null,
        EisenhowerQuadrant.UrgentImportant or EisenhowerQuadrant.ImportantNotUrgent => true,
        EisenhowerQuadrant.UrgentNotImportant or EisenhowerQuadrant.Neither => false,
        _ => throw new ArgumentException("Choose a valid urgency/importance quadrant.")
    };
    public static string Label(EisenhowerQuadrant quadrant) => quadrant switch
    {
        EisenhowerQuadrant.Unclassified => "Unclassified",
        EisenhowerQuadrant.UrgentImportant => "Urgent + important - Do first",
        EisenhowerQuadrant.ImportantNotUrgent => "Important, not urgent - Schedule",
        EisenhowerQuadrant.UrgentNotImportant => "Urgent, not important - Consider delegating",
        EisenhowerQuadrant.Neither => "Neither urgent nor important - Reconsider",
        _ => throw new ArgumentException("Choose a valid urgency/importance quadrant.")
    };
}
