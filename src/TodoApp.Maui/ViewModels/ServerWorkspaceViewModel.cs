using System.Collections.ObjectModel;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class ServerWorkspaceViewModel : ObservableViewModel, IDisposable
{
    private readonly TodoApiClient api;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private bool isBusy;
    private bool needsRefresh;
    private string errorMessage = "";
    private ApiTask? selectedTask;
    private string title = "";
    private string description = "";
    private string categoryName = "";
    private DateTime? dueDate;
    private TaskPriority priority = TaskPriority.Normal;
    private FilterOption selectedFilter;
    private int observedVersion;

    public ObservableCollection<ApiTask> Tasks { get; } = new();
    public IReadOnlyList<FilterOption> Filters { get; } = new[]
    {
        new FilterOption(TaskFilter.All, "All server tasks"), new FilterOption(TaskFilter.Pending, "Pending"),
        new FilterOption(TaskFilter.Completed, "Completed"), new FilterOption(TaskFilter.DueToday, "Due today (server)"),
        new FilterOption(TaskFilter.HighPriority, "High priority")
    };
    public IReadOnlyList<TaskPriority> Priorities { get; } = Enum.GetValues<TaskPriority>();
    public bool IsBusy => isBusy;
    public bool IsAvailable => session.Current is not null && session.IsVerified && !isBusy;
    public bool HasError => ErrorMessage.Length > 0;
    public bool NeedsRefresh => needsRefresh;
    public string EditorHeading => SelectedTask is null ? "New server task" : "Edit server task";
    public string Title { get => title; set => SetProperty(ref title, value ?? ""); }
    public string Description { get => description; set => SetProperty(ref description, value ?? ""); }
    public string CategoryName { get => categoryName; set => SetProperty(ref categoryName, value ?? ""); }
    public DateTime? DueDate { get => dueDate; set => SetProperty(ref dueDate, value); }
    public TaskPriority Priority { get => priority; set => SetProperty(ref priority, value); }
    public FilterOption SelectedFilter { get => selectedFilter; set { if (value is not null) SetProperty(ref selectedFilter, value); } }
    public string ErrorMessage
    {
        get => errorMessage;
        private set { SetProperty(ref errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }
    public ApiTask? SelectedTask
    {
        get => selectedTask;
        set
        {
            if (!SetProperty(ref selectedTask, value)) return;
            Title = value?.Title ?? "";
            Description = value?.Description ?? "";
            CategoryName = value?.CategoryName ?? "";
            DueDate = value?.DueDate?.ToDateTime(TimeOnly.MinValue);
            Priority = value?.Priority ?? TaskPriority.Normal;
            OnPropertyChanged(nameof(EditorHeading));
            UpdateCommands();
        }
    }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand ToggleCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand ClearDateCommand { get; }

    public ServerWorkspaceViewModel(TodoApiClient api, ClientSession session, ITaskNavigation navigation)
    {
        this.api = api;
        this.session = session;
        this.navigation = navigation;
        observedVersion = session.Version;
        selectedFilter = Filters[0];
        RefreshCommand = Command(RefreshAsync);
        NewCommand = Command(() => { ResetEditor(); return Task.CompletedTask; });
        SaveCommand = Command(SaveAsync, write: true);
        ToggleCommand = Command(ToggleAsync, write: true, selected: true);
        DeleteCommand = Command(DeleteAsync, write: true, selected: true);
        ClearDateCommand = Command(() => { DueDate = null; return Task.CompletedTask; });
        session.Changed += SessionChanged;
    }

    private AsyncCommand Command(Func<Task> action, bool write = false, bool selected = false) => new(
        () => RunAsync(action, write), () => IsAvailable && (!write || !needsRefresh) && (!selected || SelectedTask is not null),
        _ => ErrorMessage = "Couldn't finish the server request. Refresh before retrying.");

    private async Task RunAsync(Func<Task> action, bool write)
    {
        var version = session.Version;
        isBusy = true;
        NotifyState();
        ErrorMessage = "";
        try { await action(); }
        catch (ApiClientException exception)
        {
            if (write && session.Version == version) needsRefresh = true;
            if (session.Version == version || session.Current is null) ErrorMessage = exception.Message;
        }
        catch (Exception)
        {
            if (write && session.Version == version) needsRefresh = true;
            ErrorMessage = "Couldn't finish the server request. Refresh before retrying a write.";
        }
        finally { isBusy = false; NotifyState(); }
    }

    private async Task RefreshAsync()
    {
        var version = session.Version;
        var loaded = await api.GetTasksAsync(SelectedFilter.Value);
        session.EnsureVersion(version);
        ResetEditor();
        Tasks.Clear();
        foreach (var task in loaded) Tasks.Add(task);
        needsRefresh = false;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > TaskItem.MaxTitleLength ||
            Description.Trim().Length > TaskItem.MaxDescriptionLength || CategoryName.Trim().Length > Category.MaxNameLength ||
            !Enum.IsDefined(Priority))
        {
            ErrorMessage = "Use a title of 1–200 characters, description up to 4000, category up to 100, and a valid priority.";
            return;
        }

        var input = new ApiTaskWrite(Title, Description, DueDate.HasValue ? DateOnly.FromDateTime(DueDate.Value) : null,
            Priority, CategoryName, SelectedTask?.IsCompleted ?? false);
        var version = session.Version;
        if (SelectedTask is null) await api.CreateAsync(input);
        else await api.UpdateAsync(SelectedTask.Id, input);
        session.EnsureVersion(version);
        ResetEditor(); // A failed post-save refresh must not leave a duplicate-create draft.
        await RefreshAsync();
    }

    private async Task ToggleAsync()
    {
        var task = SelectedTask!;
        var version = session.Version;
        await api.UpdateAsync(task.Id, new ApiTaskWrite(task.Title, task.Description, task.DueDate,
            task.Priority, task.CategoryName, !task.IsCompleted));
        session.EnsureVersion(version);
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var task = SelectedTask!;
        var version = session.Version;
        if (!await navigation.ConfirmDeleteAsync(task.Title)) return;
        session.EnsureVersion(version);
        await api.DeleteAsync(task.Id);
        session.EnsureVersion(version);
        await RefreshAsync();
    }

    private void ResetEditor()
    {
        SelectedTask = null;
        Title = Description = CategoryName = "";
        DueDate = null;
        Priority = TaskPriority.Normal;
    }

    private void SessionChanged()
    {
        if (observedVersion != session.Version)
        {
            observedVersion = session.Version;
            Tasks.Clear();
            ResetEditor();
            needsRefresh = false;
            ErrorMessage = "";
        }
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(NeedsRefresh));
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); NewCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged(); ClearDateCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => session.Changed -= SessionChanged;
}
