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
    public async Task OpenTaskDetailsAsync(Guid id, ViewModels.HierarchyWorkspace workspace)
    {
        var context = new Models.TaskContext(workspace, id, services.GetRequiredService<ClientSession>().Version);
        var page = services.GetRequiredService<TaskDetailsPage>();
        await page.PrepareAsync(context);
        await GetRoot().Navigation.PushAsync(page);
    }
    public Task OpenDeviceListAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<TaskListPage>());
    public async Task OpenSyncTaskAsync(Guid id)
    {
        var page = services.GetRequiredService<SyncPage>(); page.InitialTaskId = id;
        await GetRoot().Navigation.PushAsync(page);
    }
    public Task OpenTaskFocusAsync(Models.TaskContext context)
    {
        if (context.Workspace == ViewModels.HierarchyWorkspace.SyncedAccount)
            services.GetRequiredService<ClientSession>().EnsureVersion(context.SessionVersion);
        var vm = services.GetRequiredService<ViewModels.FocusViewModel>(); vm.Workspace = context.Workspace;
        var page = new FocusPage(vm) { InitialTaskId = context.TaskId,
            ContextCurrent = () => context.Workspace == ViewModels.HierarchyWorkspace.Device || services.GetRequiredService<ClientSession>().Version == context.SessionVersion };
        return GetRoot().Navigation.PushAsync(page);
    }
    public Task OpenTaskGoogleEventsAsync(Models.TaskContext context)
    {
        services.GetRequiredService<ClientSession>().EnsureVersion(context.SessionVersion);
        var page = services.GetRequiredService<GoogleEventsPage>(); page.Context = context;
        return GetRoot().Navigation.PushAsync(page);
    }
    public Task OpenLocationRemindersAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<LocationRemindersPage>());
    public Task<bool> ConfirmLocationActionAsync(string action, string details) => GetRoot().CurrentPage.DisplayAlertAsync(action + "?", details, "Continue", "Cancel");

    public async Task EditTaskAsync(Guid? taskId)
    {
        var navigation = GetRoot();
        var page = services.GetRequiredService<TaskEditorPage>();
        await page.ViewModel.LoadAsync(taskId);
        await navigation.Navigation.PushAsync(page);
    }

    public async Task CloseEditorAsync() => await GetRoot().Navigation.PopAsync();

    public Task OpenFocusAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<FocusPage>());
    public Task OpenCalendarAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<CalendarPage>());
    public Task OpenGoogleConnectionAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<GoogleConnectionPage>());
    public Task OpenGoogleEventsAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<GoogleEventsPage>());
    public Task OpenGoogleFeedAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<GoogleFeedPage>());
    public Task<bool> ConfirmGoogleEventAsync(string action, string details) => GetRoot().CurrentPage.DisplayAlertAsync(action + "?", details, "Confirm " + action.ToLowerInvariant(), "Cancel");
    public Task<bool> ConfirmGoogleDisconnectAsync(bool revoke) => GetRoot().CurrentPage.DisplayAlertAsync(
        revoke ? "Revoke Google access?" : "Disconnect Google locally?",
        revoke ? "Revoke this Google project's granted access across its clients, then block/remove local credentials? Existing calendar events and local blocks remain."
            : "Block/remove this Todo account's local Google credentials? Google permission remains until removed in your Google Account. Existing events and blocks remain.",
        revoke ? "Revoke and disconnect" : "Disconnect locally", "Cancel");
    public Task<bool> ConfirmDiscardCalendarDraftAsync() => GetRoot().CurrentPage.DisplayAlertAsync(
        "Discard calendar draft?", "Discard unsaved time-block changes? Saved blocks remain.", "Discard draft", "Keep editing");
    public Task<bool> ConfirmDeleteCalendarBlockAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Delete local time block?", $"Remove the local block for {title}? Imported calendar copies must be removed separately. The task remains saved.", "Delete local block", "Cancel");
    public Task OpenRecurrenceAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<RecurrencePage>());
    public Task<bool> ConfirmRecurrenceConflictAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Keep local task fields?", $"Queue local fields for {title} with the displayed repeat-rule preview against the server occurrence? Completed occurrences will not be undone.", "Keep local", "Cancel");
    public Task<bool> ConfirmOccurrenceAsync(string title, DateOnly current, DateOnly next) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Complete this occurrence?", $"Complete {title} for {current:d} and move its due date to {next:d}? This task stays pending. The old due-date reminder is canceled.", "Complete and advance", "Cancel");

    public Task OpenPlanningAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<PlanningPage>());
    public Task<bool> ConfirmPlanningConflictAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Keep local task fields?", $"Queue the saved local fields for {title} with the chosen urgency/importance classification? Review the displayed server version first.", "Keep local", "Cancel");

    public Task OpenDependenciesAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<DependenciesPage>());
    public Task<bool> ConfirmDependencyConflictAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Keep local task fields?", $"Queue the saved local fields for {title} with the selected prerequisites? Review the displayed server version first.", "Keep local", "Cancel");

    public Task OpenHierarchyAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<HierarchyPage>());
    public Task<bool> ConfirmHierarchyConflictAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Keep local task fields?", $"Queue the saved local fields for {title} with the selected parent? Review the displayed server version before choosing this.", "Keep local", "Cancel");

    public Task OpenTaskViewsAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<TaskViewsPage>());
    public Task<bool> ConfirmDeleteViewAsync(string name) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Delete saved view?", $"Delete {name}? Task data will remain saved.", "Delete view", "Cancel");

    public Task OpenRemindersAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<RemindersPage>());

    public Task OpenSyncAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<SyncPage>());

    public Task OpenAccountAsync() => GetRoot().Navigation.PushAsync(services.GetRequiredService<AccountPage>());

    public Task<bool> ConfirmDeleteAsync(string title) => GetRoot().CurrentPage.DisplayAlertAsync(
        "Delete task?", $"Delete “{title}”? This cannot be undone.", "Delete", "Cancel");

    private NavigationPage GetRoot() => root ?? throw new InvalidOperationException("Navigation is not attached.");
}
