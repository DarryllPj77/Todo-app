using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel viewModel;
    public CalendarPage(CalendarViewModel viewModel)
    { this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel; }
    protected override async void OnAppearing()
    { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); }
    private async void OnWorkspaceChanged(object? sender, EventArgs args)
    { await viewModel.RefreshCommand.ExecuteAsync(); }
}
