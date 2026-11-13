using Microsoft.Extensions.DependencyInjection;

namespace TodoApp.Maui;

public partial class App : Application
{
    private readonly IServiceProvider services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
        this.services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
        var scope = services.CreateScope();
        var page = scope.ServiceProvider.GetRequiredService<Views.TaskListPage>();
        var navigation = new NavigationPage(page);
        scope.ServiceProvider.GetRequiredService<Services.TaskNavigationService>().Attach(navigation);
        var window = new Window(navigation);
        var reminders = services.GetRequiredService<Services.ReminderRuntime>();
        window.Created += async (_, _) => await reminders.AttachAsync(window.Dispatcher);
        window.Resumed += async (_, _) => await reminders.ResumeAsync();
        window.Destroying += (_, _) => { reminders.Detach(); scope.Dispose(); };
        return window;
	}
}
