using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Tests;

public sealed class SqliteLocalTaskServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoAppTests-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "tasks.db3");
    private SqliteLocalTaskService OpenService(TimeProvider? clock = null) => new(DatabasePath, clock);

    [Fact]
    public async Task ReopenedDatabase_RetainsEveryFieldAndOriginalIdentityAndTimestamps()
    {
        var service = OpenService();
        var category = new Category("Learning");
        var task = await service.CreateAsync(new TaskDetails("  Study SQLite  ", " Notes ",
            new DateOnly(2026, 11, 20), TaskPriority.High, category));
        await service.SetCompletedAsync(task.Id, true);
        var saved = (await service.GetByIdAsync(task.Id))!;

        var reopened = OpenService();
        var loaded = (await reopened.GetByIdAsync(task.Id))!;

        Assert.NotSame(saved, loaded);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("Study SQLite", loaded.Title);
        Assert.Equal("Notes", loaded.Description);
        Assert.Equal(new DateOnly(2026, 11, 20), loaded.DueDate);
        Assert.Equal(TaskPriority.High, loaded.Priority);
        Assert.True(loaded.IsCompleted);
        Assert.Equal(saved.CreatedAt, loaded.CreatedAt);
        Assert.Equal(saved.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
        Assert.Equal(category.Id, loaded.CategoryId);
        Assert.Equal("Learning", loaded.Category!.Name);
        Assert.Same(loaded, Assert.Single(loaded.Category.Tasks));
        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task EditingAndDeleting_PersistWithoutChangingPreviousSnapshots()
    {
        ILocalTaskService service = OpenService();
        var first = new Category("First");
        var second = new Category("Second");
        var original = await service.CreateAsync(new TaskDetails("Original", Category: first));
        Assert.Empty(first.Tasks); // Storage never attaches a caller's category object.

        Assert.True(await service.UpdateAsync(original.Id, new TaskDetails("Edited", "New notes",
            new DateOnly(2026, 12, 1), TaskPriority.Low, second)));
        var edited = (await OpenService().GetByIdAsync(original.Id))!;

        Assert.Equal("Original", original.Title);
        Assert.Equal(first.Id, original.CategoryId);
        Assert.Equal("Edited", edited.Title);
        Assert.Equal("New notes", edited.Description);
        Assert.Equal(new DateOnly(2026, 12, 1), edited.DueDate);
        Assert.Equal(TaskPriority.Low, edited.Priority);
        Assert.Equal(second.Id, edited.CategoryId);
        Assert.Equal(original.CreatedAt, edited.CreatedAt);
        Assert.True(await service.SetCompletedAsync(original.Id, true));
        Assert.True(await service.SetCompletedAsync(original.Id, false));
        Assert.False((await OpenService().GetByIdAsync(original.Id))!.IsCompleted);

        Assert.True(await service.UpdateAsync(original.Id, new TaskDetails("Edited")));
        var cleared = (await OpenService().GetByIdAsync(original.Id))!;
        Assert.Null(cleared.Description);
        Assert.Null(cleared.DueDate);
        Assert.Null(cleared.CategoryId);
        Assert.Equal(TaskPriority.Normal, cleared.Priority);
        Assert.True(await service.DeleteAsync(original.Id));
        Assert.Null(await OpenService().GetByIdAsync(original.Id));
        Assert.Empty(await OpenService().GetTasksAsync());
    }

    [Fact]
    public async Task EntityMutations_AreNotSavedWithoutAServiceWrite()
    {
        var service = OpenService();
        var task = await service.CreateAsync(new TaskDetails("Saved"));
        task.UpdateDetails("Unsaved", "Unsaved", null, TaskPriority.High);
        task.SetCompleted(true);

        var stored = (await service.GetByIdAsync(task.Id))!;
        Assert.Equal("Saved", stored.Title);
        Assert.Null(stored.Description);
        Assert.False(stored.IsCompleted);
    }

    [Fact]
    public async Task EquivalentUpdates_PreserveTimestampEvenWhenCategoryIsReloaded()
    {
        var service = OpenService();
        var task = await service.CreateAsync(new TaskDetails("Title", Category: new Category("Learning")));
        var loaded = (await service.GetByIdAsync(task.Id))!;

        Assert.True(await service.UpdateAsync(task.Id, new TaskDetails(" Title ", Category: loaded.Category)));
        Assert.True(await service.SetCompletedAsync(task.Id, false));

        Assert.Equal(task.UpdatedAt, (await OpenService().GetByIdAsync(task.Id))!.UpdatedAt);
    }

    [Fact]
    public async Task MissingIds_ReturnNullOrFalse()
    {
        var service = OpenService();
        var id = Guid.NewGuid();

        Assert.Null(await service.GetByIdAsync(id));
        Assert.False(await service.UpdateAsync(id, new TaskDetails("Missing")));
        Assert.False(await service.SetCompletedAsync(id, true));
        Assert.False(await service.DeleteAsync(id));
        Assert.Empty(await service.GetTasksAsync());
    }

    [Fact]
    public async Task InvalidEdits_LeavePersistedTaskAndCategoriesUnchanged()
    {
        var service = OpenService();
        var category = new Category("Keep");
        var task = await service.CreateAsync(new TaskDetails("Keep", Category: category));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(new TaskDetails(" ")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateAsync(task.Id,
            new TaskDetails("Replacement", Priority: (TaskPriority)99, Category: new Category("Not saved"))));

        var loaded = (await OpenService().GetByIdAsync(task.Id))!;
        Assert.Equal("Keep", loaded.Title);
        Assert.Equal(category.Id, loaded.CategoryId);
        Assert.Equal(task.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM TaskItems;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Categories;"));
    }

    [Theory]
    [InlineData(TaskFilter.All, "Completed high,No date,Overdue,Today high,Tomorrow")]
    [InlineData(TaskFilter.Pending, "No date,Overdue,Today high,Tomorrow")]
    [InlineData(TaskFilter.Completed, "Completed high")]
    [InlineData(TaskFilter.DueToday, "Completed high,Today high")]
    [InlineData(TaskFilter.HighPriority, "Completed high,Today high")]
    public async Task Filters_WorkAfterReopeningAndUseTheLocalDay(TaskFilter filter, string expected)
    {
        var clock = new LocalClock();
        var service = OpenService(clock);
        var today = new DateOnly(2026, 11, 14); // UTC is still November 13.
        await service.CreateAsync(new TaskDetails("Tomorrow", DueDate: today.AddDays(1)));
        await service.CreateAsync(new TaskDetails("Today high", DueDate: today, Priority: TaskPriority.High));
        await service.CreateAsync(new TaskDetails("No date"));
        await service.CreateAsync(new TaskDetails("Overdue", DueDate: today.AddDays(-1)));
        var completed = await service.CreateAsync(new TaskDetails("Completed high", DueDate: today, Priority: TaskPriority.High));
        await service.SetCompletedAsync(completed.Id, true);

        var result = await OpenService(clock).GetTasksAsync(filter);

        Assert.Equal(expected.Split(','), result.Select(task => task.Title));
    }

    [Fact]
    public async Task SharedCategory_IsLoadedOncePerGraphAndStaleNamesDoNotOverwriteStorage()
    {
        var service = OpenService();
        var category = new Category("Original name");
        var first = await service.CreateAsync(new TaskDetails("First", Category: category));
        category.Rename("Changed only in caller");
        var second = await service.CreateAsync(new TaskDetails("Second", Category: category));

        var tasks = await OpenService().GetTasksAsync();

        Assert.Same(tasks[0].Category, tasks[1].Category);
        Assert.Equal("Original name", tasks[0].Category!.Name);
        Assert.Equal(2, tasks[0].Category!.Tasks.Count);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Categories;"));
        await service.DeleteAsync(first.Id);
        var remaining = (await OpenService().GetByIdAsync(second.Id))!;
        Assert.Same(remaining, Assert.Single(remaining.Category!.Tasks));
    }

    [Fact]
    public async Task FailedTaskInsert_RollsBackNewCategoryAndLeavesCallerUntouched()
    {
        var service = OpenService();
        await service.GetTasksAsync(); // Initialize schema before installing a fault trigger.
        Execute("CREATE TRIGGER RejectTask BEFORE INSERT ON TaskItems BEGIN SELECT RAISE(ABORT, 'Test write failure'); END;");
        var category = new Category("Not saved");

        await Assert.ThrowsAsync<SqliteException>(() => service.CreateAsync(new TaskDetails("Not saved", Category: category)));

        Assert.Empty(category.Tasks);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM TaskItems;"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM Categories;"));
        Execute("DROP TRIGGER RejectTask;");
        await service.CreateAsync(new TaskDetails("Recovery"));
        Assert.Single(await OpenService().GetTasksAsync());
    }

    [Fact]
    public async Task CanceledOperations_DoNotCreateAFileOrModifyExistingTasks()
    {
        var service = OpenService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(new TaskDetails("Canceled"), token));
        Assert.False(File.Exists(DatabasePath));

        var task = await service.CreateAsync(new TaskDetails("Keep"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetTasksAsync(cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(task.Id, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(task.Id, new TaskDetails("Canceled"), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetCompletedAsync(task.Id, true, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(task.Id, token));
        var loaded = (await OpenService().GetByIdAsync(task.Id))!;
        Assert.Equal("Keep", loaded.Title);
        Assert.False(loaded.IsCompleted);
        Assert.Equal(task.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public async Task CurrentInitialization_IsRepeatableAndNeverResetsSavedData()
    {
        var task = await OpenService().CreateAsync(new TaskDetails("Keep"));
        await OpenService().GetTasksAsync();
        await OpenService().GetTasksAsync();

        Assert.Equal(3L, Scalar("PRAGMA user_version;"));
        Assert.Equal(task.Id, Assert.Single(await OpenService().GetTasksAsync()).Id);
    }

    [Fact]
    public async Task NewerSchema_IsRejectedWithoutChangingItsDataOrVersion()
    {
        Directory.CreateDirectory(directory);
        Execute("CREATE TABLE Sentinel (Value TEXT); INSERT INTO Sentinel VALUES ('Keep'); PRAGMA user_version = 4;");

        await Assert.ThrowsAsync<InvalidOperationException>(() => OpenService().GetTasksAsync());

        Assert.Equal(4L, Scalar("PRAGMA user_version;"));
        Assert.Equal("Keep", Scalar("SELECT Value FROM Sentinel;"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'TaskItems';"));
    }

    [Fact]
    public async Task Parameters_PreserveSqlLookingTextAsData()
    {
        const string title = "Read 'SQL'; DROP TABLE TaskItems; --";
        var task = await OpenService().CreateAsync(new TaskDetails(title, "Quote: ' and Unicode: 学習"));

        var loaded = (await OpenService().GetByIdAsync(task.Id))!;
        Assert.Equal(title, loaded.Title);
        Assert.Equal("Quote: ' and Unicode: 学習", loaded.Description);
        Assert.Single(await OpenService().GetTasksAsync());
    }

    [Fact]
    public async Task ForeignKey_RejectsUnknownCategoryAndClearsLinksOnCategoryDeletion()
    {
        var category = new Category("Learning");
        var task = await OpenService().CreateAsync(new TaskDetails("Keep", Category: category));
        Assert.Throws<SqliteException>(() => Execute("UPDATE TaskItems SET CategoryId = 'missing';"));
        Execute("DELETE FROM Categories;");

        var loaded = (await OpenService().GetByIdAsync(task.Id))!;
        Assert.Equal("Keep", loaded.Title);
        Assert.Null(loaded.CategoryId);
    }

    [Fact]
    public async Task SeparateServiceInstances_CanCreateTasksWithoutLosingWrites()
    {
        var writes = Enumerable.Range(1, 6)
            .Select(number => OpenService().CreateAsync(new TaskDetails($"Task {number}")));

        await Task.WhenAll(writes);

        Assert.Equal(6, (await OpenService().GetTasksAsync()).Count);
    }

    private SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private object? Scalar(string sql)
    {
        using var connection = OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Execute(string sql)
    {
        using var connection = OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            // Only this fixture's files, with no recursive deletion.
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory);
        }
    }

    private sealed class LocalClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("Test +8", TimeSpan.FromHours(8), "Test +8", "Test +8");
    }
}
