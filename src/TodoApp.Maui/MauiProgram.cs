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
        builder.Services.AddSingleton(_ => new SqliteLocalTaskService(Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")));
        builder.Services.AddSingleton<ILocalTaskService, ReminderAwareTaskService>();
        builder.Services.AddSingleton<ISecureTokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IClientSettings, ClientSettings>();
        builder.Services.AddSingleton<ClientSession>();
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        });
        builder.Services.AddSingleton<TodoApiClient>();
        builder.Services.AddSingleton(provider =>
        {
            var store = new SqliteSyncStore(Path.Combine(FileSystem.AppDataDirectory, "sync.db3"));
            store.Written += () => provider.GetRequiredService<ReminderService>().RefreshSafelyAsync();
            return store;
        });
        builder.Services.AddSingleton(_ => new SqliteReminderStore(Path.Combine(FileSystem.AppDataDirectory, "reminders.db3")));
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
