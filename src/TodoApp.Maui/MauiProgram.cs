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
        builder.Services.AddSingleton<ILocalTaskService>(_ =>
            new SqliteLocalTaskService(
                Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")));
        builder.Services.AddSingleton<ISecureTokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IClientSettings, ClientSettings>();
        builder.Services.AddSingleton<ClientSession>();
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        });
        builder.Services.AddSingleton<TodoApiClient>();
        builder.Services.AddScoped<ServerWorkspaceViewModel>();
        builder.Services.AddScoped<AccountViewModel>();
        builder.Services.AddTransient<AccountPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
