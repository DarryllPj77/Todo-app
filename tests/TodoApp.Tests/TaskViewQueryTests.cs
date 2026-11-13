using TodoApp.Domain.Enums;
using TodoApp.Domain.Views;

namespace TodoApp.Tests;

public sealed class TaskViewQueryTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static TaskViewCandidate Task(string title, int? day = null, TaskPriority priority = TaskPriority.Normal,
        bool completed = false, string? category = null, string? description = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, description, completed, day is { } offset ? Today.AddDays(offset) : null, priority, category);
    [Fact]
    public void CombinedFilters_UseAllRulesAndLiteralCaseInsensitiveSearch()
    {
        var expected = Task("Review API", 0, TaskPriority.High, category: " Work ", description: "SQL [basics]");
        var rows = new[] { expected, Task("Review API", 0, TaskPriority.Low, category: "Work", description: "SQL [basics]"),
            Task("Review API", 0, TaskPriority.High, true, "Work", "SQL [basics]"),
            Task("Review API", 0, TaskPriority.High, category: "Other", description: "SQL [basics]") };
        var definition = new TaskViewDefinition(" [BASICS] ", ViewCompletion.Pending, ViewPriority.High,
            ViewCategory.Named, " work ", ViewDue.Today);
        Assert.Equal(expected.Id, Assert.Single(TaskViewQueries.Apply(rows, definition, Today)).Id);
        Assert.Empty(TaskViewQueries.Apply(rows, new("^Review.*"), Today)); // Not regex.
    }
    [Theory]
    [InlineData(ViewDue.Any, 6)]
    [InlineData(ViewDue.Today, 1)]
    [InlineData(ViewDue.Overdue, 1)]
    [InlineData(ViewDue.NextSevenDays, 3)]
    [InlineData(ViewDue.Undated, 1)]
    [InlineData(ViewDue.Dated, 5)]
    public void DueRules_RespectCalendarBoundariesAndUndatedTasks(ViewDue due, int count)
    {
        var rows = new[] { Task("Yesterday", -1), Task("Today", 0), Task("Tomorrow", 1), Task("Day six", 6), Task("Day seven", 7), Task("Undated") };
        Assert.Equal(count, TaskViewQueries.Apply(rows, new(Due: due), Today).Count);
    }
    [Fact]
    public void DateRules_RecomputeTodayAndDoNotImplicitlyExcludeCompletedTasks()
    {
        var row = Task("Completed today", 0, completed: true);
        Assert.Single(TaskViewQueries.Apply([row], new(Due: ViewDue.Today), Today));
        Assert.Empty(TaskViewQueries.Apply([row], new(Completion: ViewCompletion.Pending, Due: ViewDue.Today), Today));
        Assert.Empty(TaskViewQueries.Apply([row], new(Due: ViewDue.Today), Today.AddDays(1)));
        var lastDay = row with { DueDate = DateOnly.MaxValue };
        Assert.Single(TaskViewQueries.Apply([lastDay], new(Due: ViewDue.NextSevenDays), DateOnly.MaxValue));
    }
    [Fact]
    public void Search_MatchesDescriptionAndCategoryAndHandlesNulls()
    {
        var rows = new[] { Task("One", description: "Needle here"), Task("Two", category: "Needle"), Task("Three") };
        Assert.Equal(2, TaskViewQueries.Apply(rows, new("needle"), Today).Count);
        Assert.Equal(3, TaskViewQueries.Apply(rows, new("  "), Today).Count);
        Assert.Equal("Three", Assert.Single(TaskViewQueries.Apply(rows, new("three"), Today)).Title);
    }
    [Fact]
    public void Category_IsExactAndSupportsUncategorizedIndependentlyOfSearch()
    {
        var rows = new[] { Task("Work", category: "Work"), Task("Workshop", category: "Workshop"), Task("None") };
        Assert.Equal("Work", Assert.Single(TaskViewQueries.Apply(rows, new(Category: ViewCategory.Named, CategoryName: "WORK"), Today)).Title);
        Assert.Equal("None", Assert.Single(TaskViewQueries.Apply(rows, new(Category: ViewCategory.Uncategorized), Today)).Title);
    }
    [Theory]
    [InlineData(ViewSort.Title, "Alpha", "Middle", "Zulu")]
    [InlineData(ViewSort.DueDate, "Middle", "Zulu", "Alpha")]
    [InlineData(ViewSort.Priority, "Zulu", "Alpha", "Middle")]
    public void Sorting_IsDeterministicWithUndatedLast(ViewSort sort, string a, string b, string c)
    {
        var rows = new[] { Task("Zulu", 2, TaskPriority.High), Task("Alpha"), Task("Middle", 0, TaskPriority.Low) };
        Assert.Equal(new[] { a, b, c }, TaskViewQueries.Apply(rows, new(Sort: sort), Today).Select(t => t.Title));
    }
    [Fact]
    public void EqualTitles_UseIdTieBreakerAndDoNotMutateSource()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var rows = new[] { Task("same", id: high), Task("SAME", id: low) };
        Assert.Equal(new[] { low, high }, TaskViewQueries.Apply(rows, new(), Today).Select(t => t.Id));
        Assert.Equal(high, rows[0].Id);
    }
    [Fact]
    public void InvalidRules_AreRejectedBeforeQuerying()
    {
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(new string('x', 201)).Normalize());
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(Category: ViewCategory.Named).Normalize());
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(CategoryName: new string('x', 101)).Normalize());
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(Due: (ViewDue)99).Normalize());
        Assert.Throws<ArgumentException>(() => new TaskViewDefinition(Sort: (ViewSort)99).Normalize());
        Assert.Equal("", new TaskViewDefinition(CategoryName: "Unused").Normalize().CategoryName);
    }
}
