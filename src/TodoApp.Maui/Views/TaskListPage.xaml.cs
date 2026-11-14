using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class TaskListPage : ContentPage
{
    private readonly TaskListViewModel viewModel;

    public TaskListPage(TaskListViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.RefreshAsync();
    }

    private async void OnFilterChanged(object? sender, EventArgs args)
    {
        await viewModel.RefreshAsync();
    }

    private void OnWorkspaceSizeChanged(object? sender, EventArgs args)
    {
        if (sender is not Grid grid || Panes is null) return;
        viewModel.SetViewportWidth(grid.Width);
        var wide = viewModel.IsWideLayout;
        Panes.ColumnDefinitions[0].Width = new GridLength(wide ? 220 : 0);
        Panes.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        Panes.ColumnDefinitions[2].Width = new GridLength(wide ? 380 : 0);
        Grid.SetColumn(DetailsPane, wide ? 2 : 1);
    }

    private async void OnToolsClicked(object? sender, EventArgs args)
    {
        var commands = new (string Label, AsyncCommand Command)[]
        {
            ("Calendar & time blocks", viewModel.CalendarCommand),
            ("Google Calendar connection", viewModel.GoogleConnectionCommand),
            ("Google event links", viewModel.GoogleEventsCommand),
            ("Google change monitoring", viewModel.GoogleFeedCommand),
            ("Location reminders", viewModel.LocationRemindersCommand),
            ("Saved views", viewModel.TaskViewsCommand), ("Task hierarchy", viewModel.HierarchyCommand),
            ("Dependencies", viewModel.DependenciesCommand), ("Urgency & importance", viewModel.PlanningCommand),
            ("Recurring tasks", viewModel.RecurrenceCommand), ("Focus & history", viewModel.FocusCommand),
            ("Reminders", viewModel.RemindersCommand), ("Synced tasks", viewModel.SyncCommand),
            ("Account & server", viewModel.AccountCommand)
        };
        var choice = await DisplayActionSheetAsync("Workspace tools", "Cancel", null, commands.Select(item => item.Label).ToArray());
        var command = commands.FirstOrDefault(item => item.Label == choice).Command;
        if (command is not null) await command.ExecuteAsync();
    }
}
