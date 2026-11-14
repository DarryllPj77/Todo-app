using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class GoogleFeedPage : ContentPage
{
    private readonly GoogleFeedViewModel viewModel;
    private readonly IDispatcherTimer timer;
    private bool visible, active;
    private Window? observedWindow;
    public GoogleFeedPage(GoogleFeedViewModel viewModel)
    {
        this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel;
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMinutes(1); timer.Tick += OnTick;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); visible = active = true; observedWindow = Window;
        if (observedWindow is not null) { observedWindow.Activated += OnActivated; observedWindow.Deactivated += OnDeactivated; observedWindow.Stopped += OnDeactivated; }
        await viewModel.RefreshCommand.ExecuteAsync(); if (!visible) return;
        timer.Start(); await PollAsync();
    }
    protected override void OnDisappearing()
    {
        visible = active = false; timer.Stop(); viewModel.Cancel();
        if (observedWindow is not null) { observedWindow.Activated -= OnActivated; observedWindow.Deactivated -= OnDeactivated; observedWindow.Stopped -= OnDeactivated; observedWindow = null; }
        base.OnDisappearing();
    }
    private void OnActivated(object? sender, EventArgs args) => active = true;
    private void OnDeactivated(object? sender, EventArgs args) { active = false; viewModel.Cancel(); }
    private async void OnTick(object? sender, EventArgs args) => await PollAsync();
    private async Task PollAsync()
    {
        if (visible && active && Connectivity.Current.NetworkAccess == NetworkAccess.Internet && viewModel.PollCommand.CanExecute(null))
            await viewModel.PollCommand.ExecuteAsync();
    }
}
