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
        if (Services.WorkspacePreview.Enabled)
        {
            window.Title = "Todo layout preview";
            window.Width = 1400;
            window.Height = 900;
            window.Destroying += (_, _) => scope.Dispose();
            return window;
        }
        var reminders = services.GetRequiredService<Services.ReminderRuntime>();
        var focus = services.GetRequiredService<Services.FocusRuntime>();
        window.Created += async (_, _) => { await reminders.AttachAsync(window.Dispatcher); await focus.AttachAsync(window); };
        window.Resumed += async (_, _) => { await reminders.ResumeAsync(); await focus.ResumedAsync(window); };
        window.Stopped += async (_, _) => await focus.StoppedAsync(window);
        window.Destroying += async (_, _) => { reminders.Detach(); await focus.DetachAsync(window); scope.Dispose(); };
        return window;
	}
}
