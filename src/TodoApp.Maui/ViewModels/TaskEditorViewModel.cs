using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class TaskEditorViewModel : ObservableViewModel
{
    private readonly ILocalTaskService tasks;
    private readonly ITaskNavigation navigation;
    private readonly List<Category> knownCategories = new();
    private Guid? taskId;
    private bool isBusy;
    private bool canSave;
    private bool isSaved;
    private string title = "";
    private string description = "";
    private string categoryName = "";
    private string errorMessage = "";
    private DateTime? dueDate;
    private TaskPriority priority = TaskPriority.Normal;

    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand ClearDueDateCommand { get; }
    public IReadOnlyList<TaskPriority> Priorities { get; } = Enum.GetValues<TaskPriority>();
    public string Heading => taskId.HasValue ? "Edit task" : "New task";
    public bool IsBusy => isBusy;
    public bool IsIdle => !isBusy;
    public bool HasError => ErrorMessage.Length > 0;
    public string Title { get => title; set => SetProperty(ref title, value ?? ""); }
    public string Description { get => description; set => SetProperty(ref description, value ?? ""); }
    public string CategoryName { get => categoryName; set => SetProperty(ref categoryName, value ?? ""); }
    public DateTime? DueDate { get => dueDate; set => SetProperty(ref dueDate, value); }
    public TaskPriority Priority { get => priority; set => SetProperty(ref priority, value); }
    public string ErrorMessage
    {
        get => errorMessage;
        private set { SetProperty(ref errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public TaskEditorViewModel(ILocalTaskService tasks, ITaskNavigation navigation)
    {
        this.tasks = tasks;
        this.navigation = navigation;
        SaveCommand = new AsyncCommand(SaveAsync, () => !IsBusy && canSave && !isSaved,
            _ => ErrorMessage = "Couldn't save the task. Please try again.");
        CancelCommand = new AsyncCommand(() => navigation.CloseEditorAsync(), () => !IsBusy,
            _ => ErrorMessage = "Couldn't return to the list. Please use Back.");
        ClearDueDateCommand = new AsyncCommand(() => { DueDate = null; return Task.CompletedTask; },
            () => !IsBusy, _ => ErrorMessage = "Couldn't clear the due date.");
    }

    public async Task LoadAsync(Guid? id)
    {
        taskId = id;
        canSave = false;
        isSaved = false;
        ErrorMessage = "";
        SetBusy(true);
        OnPropertyChanged(nameof(Heading));
        try
        {
            var all = await tasks.GetTasksAsync();
            knownCategories.Clear();
            knownCategories.AddRange(all.Where(task => task.Category is not null)
                .Select(task => task.Category!).DistinctBy(category => category.Id).OrderBy(category => category.Id));
            var task = id.HasValue ? all.FirstOrDefault(item => item.Id == id) : null;
            if (id.HasValue && task is null)
            {
                ErrorMessage = "This task no longer exists. Return to the list and refresh.";
                return;
            }

            Title = task?.Title ?? "";
            Description = task?.Description ?? "";
            CategoryName = task?.Category?.Name ?? "";
            DueDate = task?.DueDate?.ToDateTime(TimeOnly.MinValue);
            Priority = task?.Priority ?? TaskPriority.Normal;
            canSave = true;
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load task details. Return to the list and try again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        ErrorMessage = Validate();
        if (HasError)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var name = CategoryName.Trim();
            var category = name.Length == 0 ? null : knownCategories.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) ?? new Category(name);
            var details = new TaskDetails(Title, Description,
                DueDate.HasValue ? DateOnly.FromDateTime(DueDate.Value) : null, Priority, category);

            if (taskId.HasValue)
            {
                if (!await tasks.UpdateAsync(taskId.Value, details))
                {
                    canSave = false;
                    ErrorMessage = "This task no longer exists. Return to the list and refresh.";
                    return;
                }
            }
            else
            {
                var created = await tasks.CreateAsync(details);
                taskId = created.Id;
            }

            isSaved = true; // A navigation failure must not allow a duplicate create.
            try
            {
                await navigation.CloseEditorAsync();
            }
            catch (Exception)
            {
                ErrorMessage = "Task saved. Use Back to return to the list.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't save the task. Your edits are still here; please try again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > TaskItem.MaxTitleLength)
            return $"Enter a title between 1 and {TaskItem.MaxTitleLength} characters.";
        if (Description.Trim().Length > TaskItem.MaxDescriptionLength)
            return $"Keep the description within {TaskItem.MaxDescriptionLength} characters.";
        if (CategoryName.Trim().Length > Category.MaxNameLength)
            return $"Keep the category within {Category.MaxNameLength} characters.";
        if (!Enum.IsDefined(Priority))
            return "Choose a valid priority.";
        return "";
    }

    private void SetBusy(bool value)
    {
        isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ClearDueDateCommand.NotifyCanExecuteChanged();
    }
}
