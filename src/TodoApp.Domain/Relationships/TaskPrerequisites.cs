using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoApp.Domain.Relationships;

[JsonConverter(typeof(TaskPrerequisitesJsonConverter))]
public sealed class TaskPrerequisites : IReadOnlyList<Guid>, IEquatable<TaskPrerequisites>
{
    public const int MaxCount = 32;
    private readonly Guid[] ids;
    public static TaskPrerequisites Empty { get; } = new([]);
    private TaskPrerequisites(Guid[] ids) => this.ids = ids;
    public static TaskPrerequisites FromIds(IEnumerable<Guid> values)
    {
        var input = values.Take(MaxCount + 1).ToArray();
        if (input.Length > MaxCount || input.Contains(Guid.Empty))
            throw new ArgumentException("Choose at most 32 nonempty prerequisite task IDs.");
        return input.Length == 0 ? Empty : new(input.Distinct().Order().ToArray());
    }
    public int Count => ids.Length;
    public Guid this[int index] => ids[index];
    public bool Equals(TaskPrerequisites? other) => other is not null && ids.SequenceEqual(other.ids);
    public override bool Equals(object? obj) => obj is TaskPrerequisites other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var id in ids) hash.Add(id); return hash.ToHashCode(); }
    public IEnumerator<Guid> GetEnumerator() => ((IEnumerable<Guid>)ids).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class TaskPrerequisitesJsonConverter : JsonConverter<TaskPrerequisites>
{
    public override TaskPrerequisites Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > TaskPrerequisites.MaxCount)
            throw new JsonException("Prerequisites must be an array of at most 32 task IDs.");
        var ids = new List<Guid>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !item.TryGetGuid(out var id) || id == Guid.Empty)
                throw new JsonException("Every prerequisite must be a nonempty task ID.");
            ids.Add(id);
        }
        return TaskPrerequisites.FromIds(ids);
    }
    public override void Write(Utf8JsonWriter writer, TaskPrerequisites value, JsonSerializerOptions options)
    { writer.WriteStartArray(); foreach (var id in value) writer.WriteStringValue(id); writer.WriteEndArray(); }
}

public sealed record DependencyNode(Guid Id, TaskPrerequisites Prerequisites, bool Completed);
public sealed class TaskDependencyException(string message) : ArgumentException(message);

public static class TaskDependencies
{
    // Only outgoing links of the edited task change. Traverse reachable prerequisites
    // iteratively so unrelated incomplete cache pages cannot prevent local repair.
    public static void ValidateChange(IEnumerable<DependencyNode> source, Guid id, TaskPrerequisites prerequisites, bool completing = false)
    {
        var map = source.ToDictionary(t => t.Id);
        if (id == Guid.Empty || !map.ContainsKey(id)) throw new TaskDependencyException("Select an existing task in this workspace.");
        map[id] = map[id] with { Prerequisites = prerequisites };
        var active = new HashSet<Guid>(); var finished = new HashSet<Guid>();
        var stack = new Stack<(Guid Id, bool Exit)>(); stack.Push((id, false));
        while (stack.TryPop(out var item))
        {
            if (item.Exit) { active.Remove(item.Id); finished.Add(item.Id); continue; }
            if (finished.Contains(item.Id)) continue;
            if (!map.TryGetValue(item.Id, out var node))
                throw new TaskDependencyException("Prerequisites must be live tasks in this workspace/account. Sync and review missing tasks.");
            if (!active.Add(item.Id)) throw new TaskDependencyException("Dependencies cannot contain a task itself or form a cycle.");
            stack.Push((item.Id, true));
            foreach (var prerequisite in node.Prerequisites) stack.Push((prerequisite, false));
        }
        if (completing && prerequisites.Any(p => !map[p].Completed))
            throw new TaskDependencyException("Complete this task's prerequisites before marking it completed or adding unfinished prerequisites to a completed task.");
    }
    public static void ValidateDelete(IEnumerable<DependencyNode> source, Guid id)
    {
        if (source.Any(t => t.Id != id && t.Prerequisites.Contains(id)))
            throw new TaskDependencyException("Remove incoming dependency links before deleting this prerequisite task.");
    }
    public static int BlockingCount(IEnumerable<DependencyNode> source, TaskPrerequisites prerequisites)
    {
        var map = source.ToDictionary(t => t.Id);
        return prerequisites.Count(id => !map.TryGetValue(id, out var task) || !task.Completed);
    }
}
