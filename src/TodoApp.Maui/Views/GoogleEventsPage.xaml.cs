using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class GoogleEventsPage : ContentPage
{
    private readonly GoogleEventsViewModel viewModel;
    public GoogleEventsPage(GoogleEventsViewModel viewModel)
    { this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); }
    protected override void OnDisappearing() { viewModel.Cancel(); base.OnDisappearing(); }
}
