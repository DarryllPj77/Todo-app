using System.Text.Json.Serialization;

namespace TodoApp.Domain.Relationships;

// Absent Parent preserves placement; Parent with Id=null explicitly moves to root.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskParent(Guid? Id);
public sealed record TaskLink(Guid Id, Guid? ParentId);
public sealed class TaskHierarchyException(string message) : ArgumentException(message);

public static class TaskHierarchy
{
    public const int MaxLevels = 16;
    public static void ValidateMove(IEnumerable<TaskLink> source, Guid id, Guid? parentId)
    {
        var map = source.ToDictionary(t => t.Id, t => t.ParentId);
        if (id == Guid.Empty || parentId == Guid.Empty || !map.ContainsKey(id))
            throw new TaskHierarchyException("Select an existing task and a valid parent.");
        if (parentId is { } parent && !map.ContainsKey(parent))
            throw new TaskHierarchyException("The parent must be an existing task in this workspace/account. Sync and review it.");
        map[id] = parentId;
        foreach (var node in map.Keys)
        {
            var path = new HashSet<Guid>(); Guid? current = node;
            while (current is { } value)
            {
                if (!path.Add(value)) throw new TaskHierarchyException("A task cannot be its own ancestor. Choose a parent outside its subtree.");
                if (path.Count > MaxLevels) throw new TaskHierarchyException("A task hierarchy can have at most 16 levels, including the root.");
                if (!map.TryGetValue(value, out current)) throw new TaskHierarchyException("A parent is missing from this workspace. Sync and review before moving tasks.");
            }
        }
    }
    public static void ValidateDelete(IEnumerable<TaskLink> source, Guid id)
    {
        if (source.Any(t => t.ParentId == id))
            throw new TaskHierarchyException("Move or delete this task's children before deleting the parent.");
    }
}
