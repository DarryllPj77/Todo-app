using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class TaskViewModelTests
{
    [Fact]
    public async Task Editor_CreatesTaskWithAllFieldsAndReusesAnExistingCategory()
    {
        var service = new InMemoryLocalTaskService();
        var category = new Category("Work");
        await service.CreateAsync(new TaskDetails("Existing", Category: category));
        var navigation = new TestNavigation();
        var editor = new TaskEditorViewModel(service, navigation);
        await editor.LoadAsync(null);
        editor.Title = "  New task  ";
        editor.Description = " Notes ";
        editor.CategoryName = " work ";
        editor.DueDate = new DateTime(2026, 11, 20);
        editor.Priority = TaskPriority.High;

        await editor.SaveCommand.ExecuteAsync();

        var created = (await service.GetTasksAsync()).Single(task => task.Title == "New task");
        Assert.Equal("Notes", created.Description);
        Assert.Equal(category.Id, created.CategoryId);
        Assert.Equal(new DateOnly(2026, 11, 20), created.DueDate);
        Assert.Equal(TaskPriority.High, created.Priority);
        Assert.Equal(1, navigation.CloseCount);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.HasError);
    }

    [Fact]
    public async Task Editor_ChangesFieldsOnlyWhenSavedAndPreservesCompletion()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Original", "Notes", new DateOnly(2026, 11, 20)));
        await service.SetCompletedAsync(task.Id, true);
        var editor = new TaskEditorViewModel(service, new TestNavigation());
        await editor.LoadAsync(task.Id);
        editor.Title = "Edited";
        editor.Description = "";
        editor.CategoryName = "Personal";
        await editor.ClearDueDateCommand.ExecuteAsync();

        Assert.Equal("Original", task.Title);
        Assert.NotNull(task.DueDate);
        await editor.SaveCommand.ExecuteAsync();

        Assert.Equal("Edited", task.Title);
        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
        Assert.Equal("Personal", task.Category!.Name);
        Assert.True(task.IsCompleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Editor_RejectsBlankTitleWithoutWritingOrClosing(string title)
    {
        var service = new InMemoryLocalTaskService();
        var navigation = new TestNavigation();
        var editor = new TaskEditorViewModel(service, navigation);
        await editor.LoadAsync(null);
        editor.Title = title;

        await editor.SaveCommand.ExecuteAsync();

        Assert.Contains("title", editor.ErrorMessage);
        Assert.Empty(await service.GetTasksAsync());
        Assert.Equal(0, navigation.CloseCount);
        Assert.False(editor.IsBusy);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("category")]
    [InlineData("priority")]
    public async Task Editor_RejectsInvalidFieldsAndKeepsEdits(string field)
    {
        var service = new InMemoryLocalTaskService();
        var editor = new TaskEditorViewModel(service, new TestNavigation());
        await editor.LoadAsync(null);
        editor.Title = "Keep these edits";
        if (field == "title") editor.Title = new string('x', TaskItem.MaxTitleLength + 1);
        if (field == "description") editor.Description = new string('x', TaskItem.MaxDescriptionLength + 1);
        if (field == "category") editor.CategoryName = new string('x', Category.MaxNameLength + 1);
        if (field == "priority") editor.Priority = (TaskPriority)99;

        await editor.SaveCommand.ExecuteAsync();

        Assert.Contains(field, editor.ErrorMessage);
        Assert.NotEmpty(editor.Title);
        Assert.Empty(await service.GetTasksAsync());
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editor_CancelDoesNotSaveChanges()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Keep"));
        var navigation = new TestNavigation();
        var editor = new TaskEditorViewModel(service, navigation);
        await editor.LoadAsync(task.Id);
        editor.Title = "Discard";

        await editor.CancelCommand.ExecuteAsync();

        Assert.Equal("Keep", task.Title);
        Assert.Equal(1, navigation.CloseCount);
    }

    [Fact]
    public async Task Editor_MissingTaskCannotBeSavedOrRecreated()
    {
        var service = new InMemoryLocalTaskService();
        var editor = new TaskEditorViewModel(service, new TestNavigation());
        await editor.LoadAsync(Guid.NewGuid());
        Assert.Contains("no longer exists", editor.ErrorMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
        await editor.SaveCommand.ExecuteAsync();
        Assert.Empty(await service.GetTasksAsync());
    }

    [Fact]
    public async Task Editor_TaskDeletedDuringEditingIsReportedWithoutCreatingReplacement()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Original"));
        var editor = new TaskEditorViewModel(service, new TestNavigation());
        await editor.LoadAsync(task.Id);
        await service.DeleteAsync(task.Id);
        editor.Title = "Edited";

        await editor.SaveCommand.ExecuteAsync();

        Assert.Contains("no longer exists", editor.ErrorMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Empty(await service.GetTasksAsync());
    }

    [Fact]
    public async Task Editor_FailedSaveRetainsInputAndCanBeRetried()
    {
        var service = new ControlledService { FailWrites = true };
        var navigation = new TestNavigation();
        var editor = new TaskEditorViewModel(service, navigation);
        await editor.LoadAsync(null);
        editor.Title = "Retry me";

        await editor.SaveCommand.ExecuteAsync();

        Assert.True(editor.HasError);
        Assert.Equal("Retry me", editor.Title);
        Assert.Equal(0, navigation.CloseCount);
        Assert.True(editor.SaveCommand.CanExecute(null));
        service.FailWrites = false;
        await editor.SaveCommand.ExecuteAsync();
        Assert.Single(await service.GetTasksAsync());
        Assert.Equal(1, navigation.CloseCount);
    }

    [Fact]
    public async Task Editor_DoubleSaveAndFailedNavigationDoNotCreateDuplicates()
    {
        var service = new ControlledService { WriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var navigation = new TestNavigation { FailClose = true };
        var editor = new TaskEditorViewModel(service, navigation);
        await editor.LoadAsync(null);
        editor.Title = "Only once";

        var firstSave = editor.SaveCommand.ExecuteAsync();
        Assert.True(editor.IsBusy);
        await editor.SaveCommand.ExecuteAsync();
        service.WriteGate.SetResult();
        await firstSave;
        await editor.SaveCommand.ExecuteAsync();

        Assert.Single(await service.GetTasksAsync());
        Assert.Contains("Task saved", editor.ErrorMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task List_RefreshesFiltersAndCompletionAndSelectionCommands()
    {
        var service = new InMemoryLocalTaskService();
        var first = await service.CreateAsync(new TaskDetails("First", Priority: TaskPriority.High));
        await service.CreateAsync(new TaskDetails("Second"));
        var navigation = new TestNavigation();
        var list = new TaskListViewModel(service, navigation);
        await list.RefreshAsync();

        Assert.Equal("2 tasks", list.CountText);
        Assert.False(list.DeleteCommand.CanExecute(null));
        list.SelectedTask = list.Tasks.Single(task => task.Id == first.Id);
        Assert.True(list.EditCommand.CanExecute(null));
        await list.EditCommand.ExecuteAsync();
        Assert.Equal(first.Id, navigation.EditedId);
        await list.ToggleCommand.ExecuteAsync();
        Assert.True(first.IsCompleted);
        Assert.False(list.HasSelection);

        list.SelectedFilter = list.Filters.Single(filter => filter.Value == TaskFilter.Pending);
        await list.RefreshAsync();
        Assert.Equal("Second", Assert.Single(list.Tasks).Title);
        list.SelectedFilter = list.Filters.Single(filter => filter.Value == TaskFilter.Completed);
        await list.RefreshAsync();
        list.SelectedTask = Assert.Single(list.Tasks);
        Assert.Equal("Reopen", list.ToggleText);
        await list.ToggleCommand.ExecuteAsync();
        Assert.False(first.IsCompleted);
        Assert.Empty(list.Tasks);
        await list.AddCommand.ExecuteAsync();
        Assert.Null(navigation.EditedId);
    }

    [Fact]
    public async Task List_DeletionRequiresConfirmationAndRefreshesAfterSuccess()
    {
        var service = new InMemoryLocalTaskService();
        await service.CreateAsync(new TaskDetails("Delete me"));
        var navigation = new TestNavigation();
        var list = new TaskListViewModel(service, navigation);
        await list.RefreshAsync();
        list.SelectedTask = Assert.Single(list.Tasks);

        await list.DeleteCommand.ExecuteAsync();
        Assert.Single(list.Tasks);
        Assert.Single(await service.GetTasksAsync());
        navigation.ConfirmDelete = true;
        await list.DeleteCommand.ExecuteAsync();

        Assert.Empty(list.Tasks);
        Assert.False(list.HasSelection);
        Assert.Equal("0 tasks", list.CountText);
        Assert.Empty(await service.GetTasksAsync());
    }

    [Fact]
    public async Task ReadFailuresAreVisibleAndCommandsRecoverAfterRetry()
    {
        var service = new ControlledService { FailReads = true };
        var list = new TaskListViewModel(service, new TestNavigation());
        await list.RefreshAsync();
        Assert.True(list.HasError);
        Assert.True(list.RefreshCommand.CanExecute(null));
        var editor = new TaskEditorViewModel(service, new TestNavigation());
        await editor.LoadAsync(null);
        Assert.True(editor.HasError);
        Assert.False(editor.SaveCommand.CanExecute(null));

        service.FailReads = false;
        await list.RefreshAsync();
        await editor.LoadAsync(null);
        Assert.False(list.HasError);
        Assert.False(editor.HasError);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    private sealed class TestNavigation : ITaskNavigation
    {
        public Task OpenAccountAsync() => Task.CompletedTask;
        public Guid? EditedId { get; private set; }
        public int CloseCount { get; private set; }
        public bool ConfirmDelete { get; set; }
        public bool FailClose { get; set; }
        public Task EditTaskAsync(Guid? taskId) { EditedId = taskId; return Task.CompletedTask; }
        public Task CloseEditorAsync()
        {
            if (FailClose) throw new InvalidOperationException("Test navigation failure");
            CloseCount++;
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmDeleteAsync(string title) => Task.FromResult(ConfirmDelete);
    }

    private sealed class ControlledService : ILocalTaskService
    {
        private readonly InMemoryLocalTaskService inner = new();
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public TaskCompletionSource? WriteGate { get; init; }
        public Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All, CancellationToken cancellationToken = default) =>
            FailReads ? throw new IOException("Test read failure") : inner.GetTasksAsync(filter, cancellationToken);
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public async Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new IOException("Test write failure");
            if (WriteGate is not null) await WriteGate.Task;
            return await inner.CreateAsync(details, cancellationToken);
        }
        public Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken cancellationToken = default) => inner.UpdateAsync(id, details, cancellationToken);
        public Task<bool> SetCompletedAsync(Guid id, bool isCompleted, CancellationToken cancellationToken = default) => inner.SetCompletedAsync(id, isCompleted, cancellationToken);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    }
}
