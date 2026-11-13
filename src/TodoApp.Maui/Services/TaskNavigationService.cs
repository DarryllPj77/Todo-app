using TodoApp.Maui.Views;

namespace TodoApp.Maui.Services;

// One instance per window: navigation never targets another window's page.
public sealed class TaskNavigationService : ITaskNavigation
{
    private readonly IServiceProvider services;
    private NavigationPage? root;

    public TaskNavigationService(IServiceProvider services)
    {
        this.services = services;
    }

    public void Attach(NavigationPage navigationPage) => root = navigationPage;

    public async Task EditTaskAsync(Guid? taskId)
    {
        var navigation = GetRoot();
        var page = services.GetRequiredService<TaskEditorPage>();
        await page.ViewModel.LoadAsync(taskId);
        await navigation.Navigation.PushAsync(page);
    }

    public async Task CloseEditorAsync() => await GetRoot().Navigation.PopAsync();

    public Task OpenRemindersAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<RemindersPage>());

    public Task OpenSyncAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<SyncPage>());

    public Task OpenAccountAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<AccountPage>());

    public Task<bool> ConfirmDeleteAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Delete task?", $"Delete “{title}”? This cannot be undone.", "Delete", "Cancel");

    private NavigationPage GetRoot() => root ?? throw new InvalidOperationException("Navigation is not attached.");
}
