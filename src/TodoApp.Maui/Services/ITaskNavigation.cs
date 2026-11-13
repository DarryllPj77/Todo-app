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
    Task OpenHierarchyAsync();
    Task<bool> ConfirmHierarchyConflictAsync(string title);
    Task<bool> ConfirmDeleteViewAsync(string name);
}
