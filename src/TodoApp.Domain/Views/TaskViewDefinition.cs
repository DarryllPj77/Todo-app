using System.Text.Json.Serialization;
using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Views;

public enum ViewCompletion { Any, Pending, Completed }
public enum ViewPriority { Any, Low, Normal, High }
public enum ViewCategory { Any, Named, Uncategorized }
public enum ViewDue { Any, Today, Overdue, NextSevenDays, Undated, Dated }
public enum ViewSort { Title, DueDate, Priority }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskViewDefinition(string Search = "", ViewCompletion Completion = ViewCompletion.Any,
    ViewPriority Priority = ViewPriority.Any, ViewCategory Category = ViewCategory.Any,
    string CategoryName = "", ViewDue Due = ViewDue.Any, ViewSort Sort = ViewSort.Title)
{
    public TaskViewDefinition Normalize()
    {
        if (!Enum.IsDefined(Completion) || !Enum.IsDefined(Priority) || !Enum.IsDefined(Category) ||
            !Enum.IsDefined(Due) || !Enum.IsDefined(Sort)) throw new ArgumentException("Choose valid filter and sort options.");
        var search = (Search ?? "").Trim(); var category = (CategoryName ?? "").Trim();
        if (search.Length > 200) throw new ArgumentException("Search must be at most 200 characters.");
        if (category.Length > 100 || (Category == ViewCategory.Named && category.Length == 0))
            throw new ArgumentException("A named category needs 1-100 characters.");
        return this with { Search = search, CategoryName = Category == ViewCategory.Named ? category : "" };
    }
}

public sealed record TaskViewCandidate(Guid Id, string Title, string? Description, bool Completed,
    DateOnly? DueDate, TaskPriority Priority, string? CategoryName);
public sealed record SavedTaskView(Guid Id, string Scope, string Name, long Revision, TaskViewDefinition Definition);

public static class TaskViewQueries
{
    public static IReadOnlyList<TaskViewCandidate> Apply(IEnumerable<TaskViewCandidate> source,
        TaskViewDefinition definition, DateOnly today)
    {
        var filter = definition.Normalize();
        var matches = source.Where(task =>
            (filter.Search.Length == 0 || Contains(task.Title, filter.Search) || Contains(task.Description, filter.Search) || Contains(task.CategoryName, filter.Search)) &&
            (filter.Completion == ViewCompletion.Any || task.Completed == (filter.Completion == ViewCompletion.Completed)) &&
            (filter.Priority == ViewPriority.Any || task.Priority == (filter.Priority switch
                { ViewPriority.Low => TaskPriority.Low, ViewPriority.Normal => TaskPriority.Normal, _ => TaskPriority.High })) &&
            (filter.Category == ViewCategory.Any || (filter.Category == ViewCategory.Uncategorized
                ? string.IsNullOrWhiteSpace(task.CategoryName) : string.Equals(task.CategoryName?.Trim(), filter.CategoryName, StringComparison.OrdinalIgnoreCase))) &&
            MatchesDue(task.DueDate, filter.Due, today));
        IOrderedEnumerable<TaskViewCandidate> ordered = filter.Sort switch
        {
            ViewSort.DueDate => matches.OrderBy(t => t.DueDate is null).ThenBy(t => t.DueDate).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            ViewSort.Priority => matches.OrderByDescending(t => t.Priority).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            _ => matches.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(t => t.Id).ToList().AsReadOnly();
    }
    private static bool Contains(string? value, string search) => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    private static bool MatchesDue(DateOnly? date, ViewDue due, DateOnly today) => due switch
    {
        ViewDue.Today => date == today,
        ViewDue.Overdue => date < today,
        ViewDue.NextSevenDays => date is { } day && day >= today && day.DayNumber - today.DayNumber < 7,
        ViewDue.Undated => date is null,
        ViewDue.Dated => date is not null,
        _ => true
    };
}
