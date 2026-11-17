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

    private async void OnAccountTasks(object? sender, EventArgs args) => await Navigator.OpenSyncAsync();
    private async void OnTaskDetails(object? sender, EventArgs args)
    {
        if (viewModel.SelectedTask is { } task)
            await Navigator.OpenTaskDetailsAsync(task.Id, TodoApp.Maui.ViewModels.HierarchyWorkspace.Device);
    }
    private TodoApp.Maui.Services.TaskNavigationService Navigator => Handler!.MauiContext!.Services.GetRequiredService<TodoApp.Maui.Services.TaskNavigationService>();
}
