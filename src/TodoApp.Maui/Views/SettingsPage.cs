using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public sealed class SettingsPage : ContentPage
{
    public SettingsPage(IServiceProvider services, TaskNavigationService navigation)
    {
        Title = "Settings";
        var theme = new Picker { Title = "Appearance", ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = Preferences.Default.Get("appearance", "System") };
        theme.SelectedIndexChanged += (_, _) =>
        {
            var name = theme.SelectedItem as string ?? "System"; Preferences.Default.Set("appearance", name);
            if (Application.Current is { } app) app.UserAppTheme = name switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
        };
        Content = new ScrollView { Content = WorkspaceUi.Stack(WorkspaceUi.Heading("Settings"),
            WorkspaceUi.Heading("Account"), WorkspaceUi.Action("Your Todo account", navigation.OpenAccountAsync),
            WorkspaceUi.Heading("Sync"), WorkspaceUi.Note("Account tasks queue on this device. Device-only tasks are never uploaded automatically."),
            WorkspaceUi.Action("Sync status & manual sync", () => Navigation.PushAsync(services.GetRequiredService<SyncSettingsPage>())),
            WorkspaceUi.Heading("Integrations"), WorkspaceUi.Action("Google Calendar · connection", navigation.OpenGoogleConnectionAsync),
            WorkspaceUi.Action("Google Calendar · monitoring", navigation.OpenGoogleFeedAsync),
            WorkspaceUi.Action("Location permissions & delivery", () => Navigation.PushAsync(services.GetRequiredService<LocationSettingsPage>())),
            WorkspaceUi.Heading("Notifications"), WorkspaceUi.Action("Notification permission & scheduling status", () => Navigation.PushAsync(services.GetRequiredService<NotificationSettingsPage>())),
            WorkspaceUi.Heading("Appearance"), theme,
            WorkspaceUi.Heading("Advanced"), WorkspaceUi.Action("Server connection", () => Navigation.PushAsync(services.GetRequiredService<ServerSettingsPage>())) ) };
    }
}

public sealed class ServerSettingsPage : ContentPage
{
    private readonly AccountViewModel model;
    public ServerSettingsPage(AccountViewModel model)
    {
        this.model = model; BindingContext = model; Title = "Server connection";
        var address = new Entry { Keyboard = Keyboard.Url, IsTextPredictionEnabled = false }; address.SetBinding(Entry.TextProperty, "Endpoint", BindingMode.TwoWay);
        Content = new ScrollView { Content = WorkspaceUi.Stack(WorkspaceUi.Heading("Server connection"),
            WorkspaceUi.Note("Development configuration. Changing the HTTPS origin signs you out. Queued work remains under the original account and origin."),
            WorkspaceUi.Bound("ActiveEndpoint"), address,
            WorkspaceUi.Action("Apply address & sign out", () => model.SaveEndpointCommand.ExecuteAsync()),
            WorkspaceUi.Action("Verify saved session", () => model.VerifyCommand.ExecuteAsync()),
            WorkspaceUi.Bound("Status"), WorkspaceUi.Bound("ErrorMessage")) };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.InitializeAsync(); }
}

public sealed class SyncSettingsPage : ContentPage
{
    private readonly SyncWorkspaceViewModel model;
    public SyncSettingsPage(SyncWorkspaceViewModel model)
    {
        this.model = model; BindingContext = model; Title = "Sync status";
        Content = new ScrollView { Content = WorkspaceUi.Stack(WorkspaceUi.Heading("Account synchronization"),
            WorkspaceUi.Note("This status belongs to this device's active account cache. Open Tasks > My account to edit tasks and resolve conflicts."),
            WorkspaceUi.Bound("Status"), WorkspaceUi.Bound("Error"),
            WorkspaceUi.Action("Refresh status", () => model.ReloadCommand.ExecuteAsync()),
            WorkspaceUi.Action("Sync now", () => model.SyncCommand.ExecuteAsync()),
            WorkspaceUi.Action("Retry now", () => model.RetryCommand.ExecuteAsync())) };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.InitializeAsync(); }
}

public sealed class NotificationSettingsPage : RemindersPage
{
    public NotificationSettingsPage(RemindersViewModel model) : base(model)
    {
        Title = "Notifications";
        Content = new ScrollView { Content = WorkspaceUi.Stack(WorkspaceUi.Heading("Notifications"), WorkspaceUi.Bound("PlatformInfo"),
            WorkspaceUi.Bound("Status"), WorkspaceUi.Bound("Error"),
            WorkspaceUi.Note("Reminders are configured from a task and stay on this device. Permission is requested only when you choose the action below."),
            WorkspaceUi.Action("Allow / check notification permission", () => model.PermissionCommand.ExecuteAsync()),
            WorkspaceUi.Action("Retry scheduling or cancellation", () => model.RetryCommand.ExecuteAsync())) };
    }
}

public sealed class LocationSettingsPage : LocationRemindersPage
{
    public LocationSettingsPage(LocationRemindersViewModel model) : base(model)
    {
        Title = "Location & delivery";
        var workspace = new Picker { Title = "Workspace", ItemsSource = model.Workspaces.ToArray() }; workspace.SetBinding(Picker.SelectedItemProperty, "Workspace", BindingMode.TwoWay);
        workspace.SelectedIndexChanged += async (_, _) => { if (Window is not null) await model.RefreshCommand.ExecuteAsync(); };
        var place = new Picker { Title = "Saved place for this run", ItemDisplayBinding = new Binding("Label") };
        place.SetBinding(Picker.ItemsSourceProperty, "Reminders"); place.SetBinding(Picker.SelectedItemProperty, "SelectedReminder", BindingMode.TwoWay);
        var stack = WorkspaceUi.Stack(WorkspaceUi.Heading("Location permissions & delivery"),
            WorkspaceUi.Note("Configure a task's place from its details. Permission and native/background delivery are explicit device choices."), workspace, place,
            WorkspaceUi.Bound("Status"), WorkspaceUi.Bound("Error"), WorkspaceUi.Bound("NativeCapability"), WorkspaceUi.Bound("NativeStatus"), WorkspaceUi.Bound("BackgroundCapability"), WorkspaceUi.Bound("BackgroundStatus"));
        foreach (var action in new[] { ("Refresh saved delivery state", model.RefreshCommand), ("Allow/check notifications", model.PermissionCommand),
            ("Open location permission settings", model.BackgroundSettingsCommand), ("Start page-active session", model.StartNativeCommand),
            ("Stop page-active session", model.StopNativeCommand), ("Start background reminders", model.StartBackgroundCommand), ("Stop background reminders", model.StopBackgroundCommand) })
        { var command = action.Item2; var button = new Button { Text = action.Item1, Command = command }; stack.Add(button); }
        Content = new ScrollView { Content = stack };
    }
}
