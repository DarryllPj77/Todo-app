using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class LocationRemindersPage : ContentPage
{
    private readonly LocationRemindersViewModel viewModel;
    private Window? observedWindow;
    public LocationRemindersPage(LocationRemindersViewModel viewModel)
    { this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); observedWindow = Window;
        if (observedWindow is not null) { observedWindow.Deactivated += OnDeactivated; observedWindow.Stopped += OnDeactivated; }
        await viewModel.RefreshCommand.ExecuteAsync();
    }
    protected override void OnDisappearing()
    {
        viewModel.Leave();
        if (observedWindow is not null) { observedWindow.Deactivated -= OnDeactivated; observedWindow.Stopped -= OnDeactivated; observedWindow = null; }
        base.OnDisappearing();
    }
    private void OnDeactivated(object? sender, EventArgs args) => viewModel.Cancel();
}
