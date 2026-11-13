using System.Collections.ObjectModel;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class TaskListViewModel : ObservableViewModel
{
    private readonly ILocalTaskService tasks;
    private readonly ITaskNavigation navigation;
    private bool isBusy;
    private string errorMessage = "";
    private TaskRow? selectedTask;
    private FilterOption selectedFilter;

    public ObservableCollection<TaskRow> Tasks { get; } = new();
    public IReadOnlyList<FilterOption> Filters { get; } = new[]
    {
        new FilterOption(TaskFilter.All, "All tasks"),
        new FilterOption(TaskFilter.Pending, "Pending"),
        new FilterOption(TaskFilter.Completed, "Completed"),
        new FilterOption(TaskFilter.DueToday, "Due today"),
        new FilterOption(TaskFilter.HighPriority, "High priority")
    };
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand ToggleCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public bool IsBusy => isBusy;
    public bool IsIdle => !isBusy;
    public bool HasError => ErrorMessage.Length > 0;
    public bool HasSelection => SelectedTask is not null;
    public string CountText => $"{Tasks.Count} {(Tasks.Count == 1 ? "task" : "tasks")}";
    public string ToggleText => SelectedTask?.IsCompleted == true ? "Reopen" : "Complete";

    public string ErrorMessage
    {
        get => errorMessage;
        private set { SetProperty(ref errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public FilterOption SelectedFilter
    {
        get => selectedFilter;
        set
        {
            if (value is not null)
            {
                SetProperty(ref selectedFilter, value);
            }
        }
    }

    public TaskRow? SelectedTask
    {
        get => selectedTask;
        set
        {
            if (SetProperty(ref selectedTask, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ToggleText));
                UpdateCommands();
            }
        }
    }

    public TaskListViewModel(ILocalTaskService tasks, ITaskNavigation navigation)
    {
        this.tasks = tasks;
        this.navigation = navigation;
        selectedFilter = Filters[0];
        RefreshCommand = Command(LoadTasksAsync);
        AddCommand = Command(() => navigation.EditTaskAsync(null));
        EditCommand = Command(() => navigation.EditTaskAsync(SelectedTask!.Id), needsSelection: true);
        ToggleCommand = Command(ToggleAsync, needsSelection: true);
        DeleteCommand = Command(DeleteAsync, needsSelection: true);
    }

    public Task RefreshAsync() => RefreshCommand.ExecuteAsync();

    private AsyncCommand Command(Func<Task> action, bool needsSelection = false) =>
        new(() => RunAsync(action), () => !IsBusy && (!needsSelection || HasSelection),
            _ => ErrorMessage = "Something went wrong. Please try again.");

    private async Task RunAsync(Func<Task> action)
    {
        SetBusy(true);
        ErrorMessage = "";
        try
        {
            await action();
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't finish the request. Refresh to check your saved tasks, then try again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadTasksAsync()
    {
        var loaded = await tasks.GetTasksAsync(SelectedFilter.Value);
        SelectedTask = null;
        Tasks.Clear();
        foreach (var task in loaded)
        {
            Tasks.Add(new TaskRow(task));
        }

        OnPropertyChanged(nameof(CountText));
    }

    private async Task ToggleAsync()
    {
        var selected = SelectedTask!;
        if (!await tasks.SetCompletedAsync(selected.Id, !selected.IsCompleted))
        {
            ErrorMessage = "This task no longer exists. The list has been refreshed.";
        }

        await LoadTasksAsync();
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedTask!;
        if (!await navigation.ConfirmDeleteAsync(selected.Title))
        {
            return;
        }

        if (!await tasks.DeleteAsync(selected.Id))
        {
            ErrorMessage = "This task was already deleted. The list has been refreshed.";
        }

        await LoadTasksAsync();
    }

    private void SetBusy(bool value)
    {
        isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}
