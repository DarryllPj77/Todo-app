using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;

namespace TodoApp.Tests;

public sealed class TaskItemTests
{
    [Fact]
    public void Constructor_NormalizesInputAndCreatesPendingTaskWithUtcTimestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var task = new TaskItem("  Learn C#  ", "  Practice classes  ");

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal("Learn C#", task.Title);
        Assert.Equal("Practice classes", task.Description);
        Assert.False(task.IsCompleted);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Null(task.DueDate);
        Assert.Null(task.CategoryId);
        Assert.Equal(TimeSpan.Zero, task.CreatedAt.Offset);
        Assert.InRange(task.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(task.CreatedAt, task.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Constructor_RejectsMissingTitle(string? title)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TaskItem(title!));
    }

    [Fact]
    public void Constructor_AcceptsLengthLimitsAndRejectsOverlongText()
    {
        var task = new TaskItem(new string('t', TaskItem.MaxTitleLength),
            new string('d', TaskItem.MaxDescriptionLength));

        Assert.Equal(TaskItem.MaxTitleLength, task.Title.Length);
        Assert.Equal(TaskItem.MaxDescriptionLength, task.Description!.Length);
        Assert.Throws<ArgumentException>(() => new TaskItem(new string('t', TaskItem.MaxTitleLength + 1)));
        Assert.Throws<ArgumentException>(() => new TaskItem("Title", new string('d', TaskItem.MaxDescriptionLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_NormalizesEmptyDescriptionToNull(string? description)
    {
        Assert.Null(new TaskItem("Title", description).Description);
    }

    [Fact]
    public void InvalidPriority_IsRejectedBeforeTaskIsAttachedToCategory()
    {
        var category = new Category("Learning");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TaskItem("Title", priority: (TaskPriority)99, category: category));
        Assert.Empty(category.Tasks);
    }

    [Fact]
    public void UpdateDetails_ChangesEditableFieldsAndPreservesIdentityAndCreationTime()
    {
        var task = new TaskItem("Old title");
        var id = task.Id;
        var createdAt = task.CreatedAt;
        var dueDate = new DateOnly(2026, 11, 20);
        var before = DateTimeOffset.UtcNow;

        task.UpdateDetails("  New title  ", " Notes ", dueDate, TaskPriority.High);

        Assert.Equal("New title", task.Title);
        Assert.Equal("Notes", task.Description);
        Assert.Equal(dueDate, task.DueDate);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(id, task.Id);
        Assert.Equal(createdAt, task.CreatedAt);
        Assert.InRange(task.UpdatedAt, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectedEdit_LeavesExistingFieldsAndTimestampUnchanged(bool invalidDescription)
    {
        var task = new TaskItem("Original", "Keep", new DateOnly(2026, 11, 10));
        var updatedAt = task.UpdatedAt;

        Assert.ThrowsAny<ArgumentException>(() => task.UpdateDetails("Replacement",
            invalidDescription ? new string('d', TaskItem.MaxDescriptionLength + 1) : "New notes",
            null, invalidDescription ? TaskPriority.High : (TaskPriority)99));

        Assert.Equal("Original", task.Title);
        Assert.Equal("Keep", task.Description);
        Assert.Equal(new DateOnly(2026, 11, 10), task.DueDate);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Equal(updatedAt, task.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_CanClearOptionalFieldsAndAcceptPastDueDates()
    {
        var task = new TaskItem("Title", "Notes", new DateOnly(2000, 1, 1));

        task.UpdateDetails("Title", " ", null, TaskPriority.Low);

        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
        Assert.Equal(TaskPriority.Low, task.Priority);
    }

    [Fact]
    public void EquivalentEdit_DoesNotChangeTimestamp()
    {
        var task = new TaskItem("Title", "Notes");
        var updatedAt = task.UpdatedAt;

        task.UpdateDetails(" Title ", " Notes ", null, TaskPriority.Normal);

        Assert.Equal(updatedAt, task.UpdatedAt);
    }

    [Fact]
    public void Completion_CanBeSetAndReversedAndRepeatedCallsAreNoOps()
    {
        var task = new TaskItem("Title");
        var createdAt = task.CreatedAt;
        var beforeCompletion = DateTimeOffset.UtcNow;

        task.SetCompleted(true);

        Assert.True(task.IsCompleted);
        Assert.InRange(task.UpdatedAt, beforeCompletion, DateTimeOffset.UtcNow);
        var completedAt = task.UpdatedAt;
        task.SetCompleted(true);
        Assert.Equal(completedAt, task.UpdatedAt);

        var beforeReopen = DateTimeOffset.UtcNow;
        task.SetCompleted(false);

        Assert.False(task.IsCompleted);
        Assert.InRange(task.UpdatedAt, beforeReopen, DateTimeOffset.UtcNow);
        Assert.Equal(createdAt, task.CreatedAt);
        var reopenedAt = task.UpdatedAt;
        task.SetCompleted(false);
        Assert.Equal(reopenedAt, task.UpdatedAt);
    }

    [Fact]
    public void CategoryChanges_UpdateBothSidesWithoutDuplicatesAndAllowUncategorizedTasks()
    {
        var first = new Category("First");
        var second = new Category("Second");
        var task = new TaskItem("Title", category: first);

        Assert.Same(first, task.Category);
        Assert.Equal(first.Id, task.CategoryId);
        Assert.Same(task, Assert.Single(first.Tasks));

        var updatedAt = task.UpdatedAt;
        task.SetCategory(first);
        Assert.Single(first.Tasks);
        Assert.Equal(updatedAt, task.UpdatedAt);

        task.SetCategory(second);
        Assert.Empty(first.Tasks);
        Assert.Same(second, task.Category);
        Assert.Equal(second.Id, task.CategoryId);
        Assert.Same(task, Assert.Single(second.Tasks));

        task.SetCategory(null);
        Assert.Empty(second.Tasks);
        Assert.Null(task.Category);
        Assert.Null(task.CategoryId);
    }
}
