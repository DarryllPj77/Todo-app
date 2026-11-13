namespace TodoApp.Maui.Services;

public interface ITaskNavigation
{
    Task EditTaskAsync(Guid? taskId);
    Task CloseEditorAsync();
    Task<bool> ConfirmDeleteAsync(string title);
    Task OpenAccountAsync();
}
