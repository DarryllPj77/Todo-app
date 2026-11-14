using Microsoft.Extensions.Logging;

using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;
using TodoApp.Maui.Views;

namespace TodoApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

        builder.Services.AddScoped<TaskNavigationService>();
        builder.Services.AddScoped<ITaskNavigation>(provider => provider.GetRequiredService<TaskNavigationService>());
        builder.Services.AddScoped<TaskListViewModel>();
        builder.Services.AddScoped<TaskListPage>();
        builder.Services.AddTransient<TaskEditorViewModel>();
        builder.Services.AddTransient<TaskEditorPage>();
        var dataDirectory = WorkspacePreview.DirectoryPath ?? FileSystem.AppDataDirectory;
        builder.Services.AddSingleton(_ => WorkspacePreview.CreateTasks(dataDirectory));
        builder.Services.AddSingleton<ILocalTaskService, ReminderAwareTaskService>();
        builder.Services.AddSingleton<ISecureTokenStore>(_ => WorkspacePreview.Enabled ? new WorkspacePreview.Tokens() : new SecureTokenStore());
        builder.Services.AddSingleton<IClientSettings>(_ => WorkspacePreview.Enabled ? new WorkspacePreview.Settings() : new ClientSettings());
        builder.Services.AddSingleton<ClientSession>();
        builder.Services.AddSingleton(_ => new HttpClient(WorkspacePreview.Enabled ? new WorkspacePreview.OfflineHandler() : new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        });
        builder.Services.AddSingleton<TodoApiClient>();
        builder.Services.AddSingleton(provider =>
        {
            var store = new SqliteSyncStore(Path.Combine(dataDirectory, "sync.db3"));
            store.Written += () => provider.GetRequiredService<ReminderService>().RefreshSafelyAsync();
            return store;
        });
        builder.Services.AddSingleton(_ => new SqliteReminderStore(Path.Combine(dataDirectory, "reminders.db3")));
        builder.Services.AddSingleton<ReminderTaskCatalog>();
        builder.Services.AddSingleton<ReminderService>();
        builder.Services.AddSingleton<ReminderRuntime>();
#if WINDOWS
        builder.Services.AddSingleton<IReminderPlatform, Platforms.Windows.WindowsReminderPlatform>();
#elif ANDROID
        builder.Services.AddSingleton<IReminderPlatform, Platforms.Android.AndroidReminderPlatform>();
#elif IOS || MACCATALYST
        builder.Services.AddSingleton<IReminderPlatform, AppleReminderPlatform>();
#endif
        if (WorkspacePreview.Enabled) builder.Services.AddSingleton<IReminderPlatform, WorkspacePreview.Notifications>();
        builder.Services.AddSingleton(_ => new SqliteTaskViewStore(Path.Combine(dataDirectory, "views.db3")));
        builder.Services.AddSingleton(_ => new SqliteFocusStore(Path.Combine(dataDirectory, "focus.db3")));
        builder.Services.AddSingleton(_ => new SqliteCalendarStore(Path.Combine(dataDirectory, "calendar.db3")));
        builder.Services.AddSingleton<ICalendarFileExporter, CalendarFileExporter>();
        builder.Services.AddScoped<CalendarViewModel>();
        builder.Services.AddTransient<CalendarPage>();
        builder.Services.AddSingleton<IGoogleTokenVault>(_ => WorkspacePreview.Enabled ? new WorkspacePreview.GoogleVault() : new GoogleTokenVault());
#if WINDOWS
        builder.Services.AddSingleton<IGoogleAuthorizationBroker>(_ => WorkspacePreview.Enabled ? new UnsupportedGoogleBroker() : new GoogleLoopbackBroker(new GoogleSystemBrowser()));
#else
        builder.Services.AddSingleton<IGoogleAuthorizationBroker, UnsupportedGoogleBroker>();
#endif
        builder.Services.AddSingleton(_ => new GoogleOAuthProtocol(new HttpClient(WorkspacePreview.Enabled
            ? new WorkspacePreview.OfflineHandler() : new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 }));
        builder.Services.AddSingleton<GoogleConnectionService>();
        builder.Services.AddScoped<GoogleConnectionViewModel>();
        builder.Services.AddTransient<GoogleConnectionPage>();
        builder.Services.AddSingleton(_ => new GoogleCalendarApi(new HttpClient(WorkspacePreview.Enabled
            ? new WorkspacePreview.OfflineHandler() : new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 262144 }));
        builder.Services.AddSingleton<GoogleEventLinkService>();
        builder.Services.AddScoped<GoogleEventsViewModel>();
        builder.Services.AddTransient<GoogleEventsPage>();
        builder.Services.AddSingleton<IFocusTaskSource, FocusTaskCatalog>();
        builder.Services.AddSingleton<FocusService>();
        builder.Services.AddSingleton<FocusRuntime>();
        builder.Services.AddScoped<FocusViewModel>();
        builder.Services.AddTransient<FocusPage>();
        builder.Services.AddScoped<RecurrenceViewModel>();
        builder.Services.AddTransient<RecurrencePage>();
        builder.Services.AddScoped<PlanningViewModel>();
        builder.Services.AddTransient<PlanningPage>();
        builder.Services.AddScoped<DependenciesViewModel>();
        builder.Services.AddTransient<DependenciesPage>();
        builder.Services.AddScoped<HierarchyViewModel>();
        builder.Services.AddTransient<HierarchyPage>();
        builder.Services.AddScoped<TaskViewsViewModel>();
        builder.Services.AddTransient<TaskViewsPage>();
        builder.Services.AddScoped<RemindersViewModel>();
        builder.Services.AddTransient<RemindersPage>();
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddScoped<SyncWorkspaceViewModel>();
        builder.Services.AddTransient<SyncPage>();
        builder.Services.AddScoped<ServerWorkspaceViewModel>();
        builder.Services.AddScoped<AccountViewModel>();
        builder.Services.AddTransient<AccountPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
