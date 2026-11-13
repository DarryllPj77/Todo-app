using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Entities;

public sealed class TaskItem
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public bool IsCompleted { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public TaskPriority Priority { get; private set; }
    public Category? Category { get; private set; }
    public Guid? CategoryId => Category?.Id;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public TaskItem(
        string title,
        string? description = null,
        DateOnly? dueDate = null,
        TaskPriority priority = TaskPriority.Normal,
        Category? category = null)
    {
        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        ValidatePriority(priority);
        DueDate = dueDate;
        Priority = priority;
        Category = category;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        category?.AttachTask(this);
    }

    internal static TaskItem Restore(Guid id, string title, string? description, DateOnly? dueDate,
        TaskPriority priority, Category? category, bool isCompleted,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stored task must have an ID.", nameof(id));
        }

        return new TaskItem(id, title, description, dueDate, priority, category,
            isCompleted, createdAt, updatedAt);
    }

    private TaskItem(Guid id, string title, string? description, DateOnly? dueDate,
        TaskPriority priority, Category? category, bool isCompleted,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        : this(title, description, dueDate, priority, category)
    {
        Id = id;
        IsCompleted = isCompleted;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void UpdateDetails(string title, string? description, DateOnly? dueDate, TaskPriority priority)
    {
        // Validate everything first: a rejected edit must not partially change the task.
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        ValidatePriority(priority);

        if (Title == normalizedTitle && Description == normalizedDescription &&
            DueDate == dueDate && Priority == priority)
        {
            return;
        }

        Title = normalizedTitle;
        Description = normalizedDescription;
        DueDate = dueDate;
        Priority = priority;
        UpdatedAt = DateTimeOffset.UtcNow;
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
        if (Category == category)
        {
            return;
        }

        Category?.DetachTask(this);
        Category = category;
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
