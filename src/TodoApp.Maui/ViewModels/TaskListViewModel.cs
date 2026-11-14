using System.Collections.ObjectModel;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Maui.ViewModels;

public sealed class TaskListViewModel : ObservableViewModel
{
    private readonly ILocalTaskService tasks;
    private readonly ITaskNavigation navigation;
    private bool isBusy;
    private string errorMessage = "";
    private TaskRow? selectedTask;
    private FilterOption selectedFilter;
    private IReadOnlyList<TaskRow> loadedRows = [];
    private WorkspaceCategory? selectedCategory;
    private string searchText = "";
    private bool isWideLayout;
    private bool showingDetails;

    public ObservableCollection<WorkspaceCategory> Categories { get; } = new();
    public bool IsWideLayout => isWideLayout;
    public bool IsCompactLayout => !isWideLayout;
    public bool IsTaskPaneVisible => isWideLayout || !showingDetails;
    public bool IsDetailsPaneVisible => isWideLayout || showingDetails;
    public string DetailsHeading => SelectedTask?.Title ?? "Task details";
    public AsyncCommand ShowTasksCommand { get; }
    public AsyncCommand ShowDetailsCommand { get; }

    public WorkspaceCategory? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (SetProperty(ref selectedCategory, value)) ApplyQuery();
        }
    }

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value ?? "")) ApplyQuery(); }
    }

    // Logical pixels, independent of platform/device names and orientation.
    public void SetViewportWidth(double width)
    {
        if (SetProperty(ref isWideLayout, width >= 1180, nameof(IsWideLayout))) NotifyPanes();
    }

    private void NotifyPanes()
    {
        OnPropertyChanged(nameof(IsCompactLayout));
        OnPropertyChanged(nameof(IsTaskPaneVisible));
        OnPropertyChanged(nameof(IsDetailsPaneVisible));
    }

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
    public AsyncCommand FocusCommand { get; }
    public AsyncCommand CalendarCommand { get; }
    public AsyncCommand GoogleConnectionCommand { get; }
    public AsyncCommand GoogleEventsCommand { get; }
    public AsyncCommand GoogleFeedCommand { get; }
    public AsyncCommand LocationRemindersCommand { get; }
    public AsyncCommand RecurrenceCommand { get; }
    public AsyncCommand PlanningCommand { get; }
    public AsyncCommand DependenciesCommand { get; }
    public AsyncCommand HierarchyCommand { get; }
    public AsyncCommand TaskViewsCommand { get; }
    public AsyncCommand RemindersCommand { get; }
    public AsyncCommand SyncCommand { get; }
    public AsyncCommand AccountCommand { get; }
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
                OnPropertyChanged(nameof(DetailsHeading));
                if (value is null) { showingDetails = false; NotifyPanes(); }
                UpdateCommands();
            }
        }
    }

    public TaskListViewModel(ILocalTaskService tasks, ITaskNavigation navigation)
    {
        this.tasks = tasks;
        this.navigation = navigation;
        selectedFilter = Filters[0];
        ShowTasksCommand = new AsyncCommand(() => { showingDetails = false; NotifyPanes(); return Task.CompletedTask; }, () => true, _ => { });
        ShowDetailsCommand = new AsyncCommand(() => { showingDetails = true; NotifyPanes(); return Task.CompletedTask; }, () => HasSelection, _ => { });
        RefreshCommand = Command(LoadTasksAsync);
        AddCommand = Command(() => navigation.EditTaskAsync(null));
        EditCommand = Command(() => navigation.EditTaskAsync(SelectedTask!.Id), needsSelection: true);
        ToggleCommand = Command(ToggleAsync, needsSelection: true);
        DeleteCommand = Command(DeleteAsync, needsSelection: true);
        FocusCommand = Command(navigation.OpenFocusAsync);
        CalendarCommand = Command(navigation.OpenCalendarAsync);
        GoogleConnectionCommand = Command(navigation.OpenGoogleConnectionAsync);
        GoogleEventsCommand = Command(navigation.OpenGoogleEventsAsync);
        GoogleFeedCommand = Command(navigation.OpenGoogleFeedAsync);
        LocationRemindersCommand = Command(navigation.OpenLocationRemindersAsync);
        RecurrenceCommand = Command(navigation.OpenRecurrenceAsync);
        PlanningCommand = Command(navigation.OpenPlanningAsync);
        DependenciesCommand = Command(navigation.OpenDependenciesAsync);
        HierarchyCommand = Command(navigation.OpenHierarchyAsync);
        TaskViewsCommand = Command(navigation.OpenTaskViewsAsync);
        RemindersCommand = Command(navigation.OpenRemindersAsync);
        SyncCommand = Command(navigation.OpenSyncAsync);
        AccountCommand = Command(navigation.OpenAccountAsync);
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
        catch (ArgumentException error) when (error is TaskHierarchyException or TaskDependencyException or TaskRecurrenceException) { ErrorMessage = error.Message; }
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
        loadedRows = loaded.Select(task => new TaskRow(task)).ToArray();
        var prior = SelectedCategory;
        Categories.Clear();
        Categories.Add(new WorkspaceCategory(null, loadedRows.Count, IsAll: true));
        foreach (var group in loadedRows.GroupBy(row => row.CategoryName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            Categories.Add(new WorkspaceCategory(group.Key, group.Count()));
        // Set directly: replacing picker items can send a transient null selection.
        selectedCategory = Categories.FirstOrDefault(category => prior is not null && category.IsAll == prior.IsAll
            && StringComparer.OrdinalIgnoreCase.Equals(category.Name, prior.Name)) ?? Categories[0];
        OnPropertyChanged(nameof(SelectedCategory));
        ApplyQuery();
    }

    private void ApplyQuery()
    {
        var id = SelectedTask?.Id;
        var wasShowingDetails = showingDetails;
        var query = SearchText.Trim();
        var visible = loadedRows.Where(row => (SelectedCategory?.Matches(row) ?? true)
            && (query.Length == 0 || row.SearchableText.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        Tasks.Clear();
        foreach (var row in visible) Tasks.Add(row);
        SelectedTask = Tasks.FirstOrDefault(row => row.Id == id);
        showingDetails = wasShowingDetails && HasSelection;
        NotifyPanes();
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
        ShowDetailsCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        AccountCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
        RemindersCommand.NotifyCanExecuteChanged();
        TaskViewsCommand.NotifyCanExecuteChanged();
        FocusCommand.NotifyCanExecuteChanged();
        CalendarCommand.NotifyCanExecuteChanged();
        GoogleConnectionCommand.NotifyCanExecuteChanged();
        GoogleEventsCommand.NotifyCanExecuteChanged();
        GoogleFeedCommand.NotifyCanExecuteChanged();
        LocationRemindersCommand.NotifyCanExecuteChanged();
        RecurrenceCommand.NotifyCanExecuteChanged();
        PlanningCommand.NotifyCanExecuteChanged();
        DependenciesCommand.NotifyCanExecuteChanged();
        HierarchyCommand.NotifyCanExecuteChanged();
    }
}
