using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;

namespace TodoApp.Domain.Entities;

public sealed class TaskItem
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;

    public TagSet Tags { get; private set; } = TagSet.Empty;
    public Guid? ParentTaskId { get; private set; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public bool IsCompleted { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public TaskPriority Priority { get; private set; }
    public Category? Category { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public TaskItem(
        string title,
        string? description = null,
        DateOnly? dueDate = null,
        TaskPriority priority = TaskPriority.Normal,
        Category? category = null, TagSet? tags = null)
    {
        Tags = tags ?? TagSet.Empty;
        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        ValidatePriority(priority);
        DueDate = dueDate;
        Priority = priority;
        Category = category;
        CategoryId = category?.Id;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        category?.AttachTask(this);
    }

    public static TaskItem CreateWithId(Guid id, string title, string? description = null,
        DateOnly? dueDate = null, TaskPriority priority = TaskPriority.Normal, Category? category = null, TagSet? tags = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A task must have an ID.", nameof(id));
        return new TaskItem(title, description, dueDate, priority, category, tags) { Id = id };
    }

    // EF materialization only; application code uses the validating constructor.
    private TaskItem()
    {
        Title = null!;
    }

    internal static TaskItem Restore(Guid id, string title, string? description, DateOnly? dueDate,
        TaskPriority priority, Category? category, bool isCompleted,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, TagSet? tags = null, Guid? parentTaskId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stored task must have an ID.", nameof(id));
        }

        return new TaskItem(id, title, description, dueDate, priority, category,
            isCompleted, createdAt, updatedAt, tags, parentTaskId);
    }

    private TaskItem(Guid id, string title, string? description, DateOnly? dueDate,
        TaskPriority priority, Category? category, bool isCompleted,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, TagSet? tags = null, Guid? parentTaskId = null)
        : this(title, description, dueDate, priority, category, tags)
    {
        Id = id;
        SetParent(new TaskParent(parentTaskId));
        IsCompleted = isCompleted;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void UpdateDetails(string title, string? description, DateOnly? dueDate, TaskPriority priority, TagSet? tags = null)
    {
        // Validate everything first: a rejected edit must not partially change the task.
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        ValidatePriority(priority);

        if (Title == normalizedTitle && Description == normalizedDescription &&
            DueDate == dueDate && Priority == priority && (tags is null || Tags.Equals(tags)))
        {
            return;
        }

        if (tags is not null) Tags = tags;
        Title = normalizedTitle;
        Description = normalizedDescription;
        DueDate = dueDate;
        Priority = priority;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetParent(TaskParent parent)
    {
        if (parent.Id == Guid.Empty || parent.Id == Id) throw new TaskHierarchyException("A task cannot be its own parent or use an empty ID.");
        if (ParentTaskId == parent.Id) return;
        ParentTaskId = parent.Id; UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetTags(TagSet tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (Tags.Equals(tags)) return;
        Tags = tags; UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCompleted(bool isCompleted)
    {
        if (IsCompleted == isCompleted)
        {
            return;
        }

        IsCompleted = isCompleted;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCategory(Category? category)
    {
        if (Category == category && CategoryId == category?.Id)
        {
            return;
        }

        Category?.DetachTask(this);
        Category = category;
        CategoryId = category?.Id;
        category?.AttachTask(this);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalizedTitle = title.Trim();

        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title must be {MaxTitleLength} characters or fewer.", nameof(title));
        }

        return normalizedTitle;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Description must be {MaxDescriptionLength} characters or fewer.", nameof(description));
        }

        return normalizedDescription;
    }

    private static void ValidatePriority(TaskPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Choose Low, Normal, or High priority.");
        }
    }
}
