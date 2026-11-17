using Microsoft.Extensions.DependencyInjection;

namespace TodoApp.Maui;

public partial class App : Application
{
    private readonly IServiceProvider services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
        UserAppTheme = Preferences.Default.Get("appearance", "System") switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
        this.services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
        var scope = services.CreateScope();
        var page = scope.ServiceProvider.GetRequiredService<Views.WorkspacePage>();
        var window = new Window(page);
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
