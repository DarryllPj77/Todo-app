using Microsoft.Extensions.Logging;

using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

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

        builder.Services.AddTransient<ViewModels.WelcomeViewModel>();
        builder.Services.AddTransient<Views.WelcomePage>();
        builder.Services.AddSingleton<ILocalTaskService>(_ =>
            new SqliteLocalTaskService(
                Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")));

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
