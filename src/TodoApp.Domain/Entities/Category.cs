namespace TodoApp.Domain.Entities;

public sealed class Category
{
    public const int MaxNameLength = 100;

    private readonly List<TaskItem> tasks = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; private set; }
    public IReadOnlyCollection<TaskItem> Tasks => tasks.AsReadOnly();

    public Category(string name)
    {
        Name = NormalizeName(name);
    }

    public void Rename(string name)
    {
        Name = NormalizeName(name);
    }

    // TaskItem owns relationship changes so both directions stay consistent.
    internal void AttachTask(TaskItem task)
    {
        tasks.Add(task);
    }

    internal void DetachTask(TaskItem task)
    {
        tasks.Remove(task);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();

        if (normalizedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Category name must be {MaxNameLength} characters or fewer.", nameof(name));
        }

        return normalizedName;
    }
}
