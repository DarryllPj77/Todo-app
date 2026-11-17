using TodoApp.Maui.Services;

namespace TodoApp.Maui.Views;

// One native adaptive drawer and independent NavigationPage stacks per window.
public sealed class WorkspacePage : FlyoutPage
{
    private readonly IServiceProvider services;
    private readonly TaskNavigationService navigator;
    private readonly Dictionary<string, NavigationPage> sections = new();
    private readonly Dictionary<string, Button> links = new();
    public WorkspacePage(IServiceProvider services, TaskNavigationService navigator)
    {
        this.services = services; this.navigator = navigator;
        Title = "Todo";
        var menu = WorkspaceUi.Stack(WorkspaceUi.Heading("Todo"), WorkspaceUi.Note("Make room for what matters."));
        foreach (var name in new[] { "Today", "Tasks", "Calendar", "Focus", "Views", "Settings" })
        {
            var link = WorkspaceUi.Action(name, () => { ShowSection(name); return Task.CompletedTask; });
            link.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
            link.CornerRadius = 8; links.Add(name, link); menu.Add(link);
        }
        Flyout = new ContentPage { Title = "Navigation", Content = new ScrollView { Content = menu } };
        ShowSection("Tasks");
        SizeChanged += (_, _) => FlyoutLayoutBehavior = Width >= 1000 ? FlyoutLayoutBehavior.Split : FlyoutLayoutBehavior.Popover;
    }
    public void ShowSection(string section)
    {
        if (!sections.TryGetValue(section, out var navigation))
        {
            ContentPage page = section switch
            {
                "Today" => services.GetRequiredService<TodayPage>(),
                "Calendar" => services.GetRequiredService<CalendarPage>(),
                "Focus" => services.GetRequiredService<FocusPage>(),
                "Views" => services.GetRequiredService<ViewsPage>(),
                "Settings" => services.GetRequiredService<SettingsPage>(),
                _ => services.GetRequiredService<TaskListPage>()
            };
            navigation = new NavigationPage(page); sections.Add(section, navigation);
        }
        navigator.Attach(navigation); Detail = navigation;
        foreach (var pair in links)
            pair.Value.SetAppThemeColor(Button.BackgroundColorProperty,
                pair.Key == section ? Color.FromArgb("#DFEAFF") : Colors.Transparent,
                pair.Key == section ? Color.FromArgb("#243D60") : Colors.Transparent);
        if (Width < 1000) IsPresented = false;
    }
}
