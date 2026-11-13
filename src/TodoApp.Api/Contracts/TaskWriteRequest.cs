using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;

namespace TodoApp.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TaskWriteRequest : IValidatableObject
{
    public TaskPrerequisites? Prerequisites { get; init; }
    public TaskParent? Parent { get; init; }
    public TagSet? Tags { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateOnly? DueDate { get; init; }
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public string? CategoryName { get; init; }
    public bool IsCompleted { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > TaskItem.MaxTitleLength)
            yield return new ValidationResult($"Enter a title between 1 and {TaskItem.MaxTitleLength} characters.", new[] { nameof(Title) });
        if (Description?.Trim().Length > TaskItem.MaxDescriptionLength)
            yield return new ValidationResult($"Description must be {TaskItem.MaxDescriptionLength} characters or fewer.", new[] { nameof(Description) });
        if (CategoryName?.Trim().Length > Category.MaxNameLength)
            yield return new ValidationResult($"Category name must be {Category.MaxNameLength} characters or fewer.", new[] { nameof(CategoryName) });
        if (Parent?.Id == Guid.Empty) yield return new ValidationResult("Choose a valid parent ID or explicit root.", new[] { nameof(Parent) });
        if (!Enum.IsDefined(Priority))
            yield return new ValidationResult("Choose Low, Normal, or High priority.", new[] { nameof(Priority) });
    }
}
