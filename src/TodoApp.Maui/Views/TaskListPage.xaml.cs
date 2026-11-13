using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class TaskListPage : ContentPage
{
    private readonly TaskListViewModel viewModel;

    public TaskListPage(TaskListViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
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
}
