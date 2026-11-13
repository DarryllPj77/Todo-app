using TodoApp.Domain.Enums;

namespace TodoApp.Maui.Models;

public sealed record QuadrantOption(EisenhowerQuadrant? Value, string Label)
{
    public static IReadOnlyList<QuadrantOption> Classifications { get; } = Enum.GetValues<EisenhowerQuadrant>().Select(q => new QuadrantOption(q, Eisenhower.Label(q))).ToArray();
    public static IReadOnlyList<QuadrantOption> Filters { get; } = new[] { new QuadrantOption(null, "All quadrants") }.Concat(Classifications).ToArray();
}
