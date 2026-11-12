using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Tests;

public sealed class LocalTaskServiceTests
{
    [Fact]
    public async Task CrudWorkflow_PreservesIdentityAndCompletionAndUpdatesCategoryLinks()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var learning = new Category("Learning");
        var work = new Category("Work");
        var task = await service.CreateAsync(new TaskDetails("  Study  ", Category: learning));
        var id = task.Id;
        var createdAt = task.CreatedAt;

        Assert.Equal("Study", task.Title);
        Assert.Equal(id, (await service.GetByIdAsync(id))!.Id);
        Assert.Single(await service.GetTasksAsync());
        Assert.Same(task, Assert.Single(learning.Tasks));

        Assert.True(await service.SetCompletedAsync(id, true));
        var dueDate = new DateOnly(2026, 11, 20);
        Assert.True(await service.UpdateAsync(id,
            new TaskDetails("  Finish  ", " Notes ", dueDate, TaskPriority.High, work)));
        var edited = (await service.GetByIdAsync(id))!;

        Assert.Equal("Finish", edited.Title);
        Assert.Equal("Notes", edited.Description);
        Assert.Equal(dueDate, edited.DueDate);
        Assert.Equal(TaskPriority.High, edited.Priority);
        Assert.True(edited.IsCompleted);
        Assert.Equal(createdAt, edited.CreatedAt);
        Assert.Empty(learning.Tasks);
        Assert.Same(edited, Assert.Single(work.Tasks));

        Assert.True(await service.SetCompletedAsync(id, false));
        Assert.False((await service.GetByIdAsync(id))!.IsCompleted);
        Assert.True(await service.DeleteAsync(id));
        Assert.Null(await service.GetByIdAsync(id));
        Assert.Empty(await service.GetTasksAsync());
        Assert.Empty(work.Tasks);
        Assert.False(await service.DeleteAsync(id));
    }

    [Fact]
    public async Task MissingTask_ReturnsNullOrFalseWithoutCreatingData()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var missingId = Guid.NewGuid();

        Assert.Null(await service.GetByIdAsync(missingId));
        Assert.False(await service.UpdateAsync(missingId, new TaskDetails("Missing")));
        Assert.False(await service.SetCompletedAsync(missingId, true));
        Assert.False(await service.DeleteAsync(missingId));
        Assert.Empty(await service.GetTasksAsync());
    }

    [Fact]
    public async Task RejectedWrites_DoNotCreateTasksOrPartiallyEditExistingTask()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var first = new Category("First");
        var second = new Category("Second");

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.CreateAsync(new TaskDetails(" ", Category: first)));
        Assert.Empty(await service.GetTasksAsync());
        Assert.Empty(first.Tasks);

        var task = await service.CreateAsync(new TaskDetails("Original", Category: first));
        var updatedAt = task.UpdatedAt;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateAsync(task.Id,
            new TaskDetails("Replacement", Priority: (TaskPriority)99, Category: second)));

        Assert.Equal("Original", task.Title);
        Assert.Equal(updatedAt, task.UpdatedAt);
        Assert.Same(first, task.Category);
        Assert.Single(first.Tasks);
        Assert.Empty(second.Tasks);
        Assert.Single(await service.GetTasksAsync());
    }

    [Fact]
    public async Task Update_CanClearOptionalFieldsAndCategory()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var category = new Category("Learning");
        var task = await service.CreateAsync(new TaskDetails("Title", "Notes",
            new DateOnly(2026, 11, 20), TaskPriority.High, category));

        Assert.True(await service.UpdateAsync(task.Id, new TaskDetails("Title")));

        var edited = (await service.GetByIdAsync(task.Id))!;
        Assert.Null(edited.Description);
        Assert.Null(edited.DueDate);
        Assert.Null(edited.CategoryId);
        Assert.Equal(TaskPriority.Normal, edited.Priority);
        Assert.Empty(category.Tasks);
    }

    [Theory]
    [InlineData(TaskFilter.All, "Completed high,No due date,Overdue,Today high,Tomorrow")]
    [InlineData(TaskFilter.Pending, "No due date,Overdue,Today high,Tomorrow")]
    [InlineData(TaskFilter.Completed, "Completed high")]
    [InlineData(TaskFilter.DueToday, "Completed high,Today high")]
    [InlineData(TaskFilter.HighPriority, "Completed high,Today high")]
    public async Task Filters_SelectExpectedTasksAndReturnAlphabeticalResults(TaskFilter filter, string expected)
    {
        // UTC is still Nov 13, but the device's UTC+8 calendar day is Nov 14.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero), 8);
        ILocalTaskService service = new InMemoryLocalTaskService(clock);
        var today = new DateOnly(2026, 11, 14);
        await service.CreateAsync(new TaskDetails("Tomorrow", DueDate: today.AddDays(1)));
        await service.CreateAsync(new TaskDetails("Today high", DueDate: today, Priority: TaskPriority.High));
        await service.CreateAsync(new TaskDetails("No due date"));
        await service.CreateAsync(new TaskDetails("Overdue", DueDate: today.AddDays(-1)));
        var completed = await service.CreateAsync(new TaskDetails("Completed high", DueDate: today,
            Priority: TaskPriority.High));
        await service.SetCompletedAsync(completed.Id, true);

        var results = await service.GetTasksAsync(filter);

        Assert.Equal(expected.Split(','), results.Select(task => task.Title));
    }

    [Theory]
    [InlineData(8, 14)]
    [InlineData(-8, 13)]
    public async Task DueToday_UsesLocalCalendarDayAndReevaluatesAfterMidnight(int offsetHours, int day)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 30, 0, TimeSpan.Zero), offsetHours);
        ILocalTaskService service = new InMemoryLocalTaskService(clock);
        var localToday = new DateOnly(2026, 11, day);
        var first = await service.CreateAsync(new TaskDetails("First day", DueDate: localToday));
        var next = await service.CreateAsync(new TaskDetails("Next day", DueDate: localToday.AddDays(1)));

        Assert.Equal(first.Id, Assert.Single(await service.GetTasksAsync(TaskFilter.DueToday)).Id);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(next.Id, Assert.Single(await service.GetTasksAsync(TaskFilter.DueToday)).Id);
    }

    [Fact]
    public async Task QueryResults_HaveSnapshotMembershipAndCannotModifyTheStoreCollection()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var first = await service.CreateAsync(new TaskDetails("First"));
        var snapshot = await service.GetTasksAsync();
        await service.CreateAsync(new TaskDetails("Second"));
        await service.DeleteAsync(first.Id);

        Assert.Equal(first.Id, Assert.Single(snapshot).Id);
        Assert.Equal("Second", Assert.Single(await service.GetTasksAsync()).Title);
        Assert.Throws<NotSupportedException>(() => ((IList<TaskItem>)snapshot).Clear());
    }

    [Fact]
    public async Task CanceledOperations_DoNotReadOrMutateTheStore()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Keep"));
        var updatedAt = task.UpdatedAt;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetTasksAsync(cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(task.Id, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(new TaskDetails("Canceled"), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(task.Id, new TaskDetails("Canceled"), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetCompletedAsync(task.Id, true, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(task.Id, token));

        Assert.Equal("Keep", task.Title);
        Assert.False(task.IsCompleted);
        Assert.Equal(updatedAt, task.UpdatedAt);
        Assert.Single(await service.GetTasksAsync());
    }

    [Fact]
    public async Task InvalidFilterAndNullInputs_AreRejected()
    {
        ILocalTaskService service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Keep"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetTasksAsync((TaskFilter)99));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateAsync(task.Id, null!));
        Assert.Equal("Keep", (await service.GetByIdAsync(task.Id))!.Title);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow;
        private readonly TimeZoneInfo timeZone;

        public FixedTimeProvider(DateTimeOffset utcNow, int offsetHours)
        {
            this.utcNow = utcNow;
            timeZone = TimeZoneInfo.CreateCustomTimeZone("Test local time", TimeSpan.FromHours(offsetHours),
                "Test local time", "Test local time");
        }

        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => timeZone;
        public void Advance(TimeSpan elapsed) => utcNow += elapsed;
    }
}
