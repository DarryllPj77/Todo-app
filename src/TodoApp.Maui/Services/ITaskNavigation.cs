namespace TodoApp.Maui.Services;

public interface ITaskNavigation
{
    Task EditTaskAsync(Guid? taskId);
    Task CloseEditorAsync();
    Task<bool> ConfirmDeleteAsync(string title);
    Task OpenAccountAsync();
    Task OpenSyncAsync();
    Task OpenRemindersAsync();
    Task OpenTaskViewsAsync();
    Task OpenFocusAsync();
    Task OpenRecurrenceAsync();
    Task<bool> ConfirmRecurrenceConflictAsync(string title);
    Task<bool> ConfirmOccurrenceAsync(string title, DateOnly current, DateOnly next);
    Task OpenPlanningAsync();
    Task<bool> ConfirmPlanningConflictAsync(string title);
    Task OpenDependenciesAsync();
    Task<bool> ConfirmDependencyConflictAsync(string title);
    Task OpenHierarchyAsync();
    Task<bool> ConfirmHierarchyConflictAsync(string title);
    Task<bool> ConfirmDeleteViewAsync(string name);
}
