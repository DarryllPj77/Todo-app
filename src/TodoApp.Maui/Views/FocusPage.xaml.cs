using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class FocusPage : ContentPage
{
    private readonly FocusViewModel viewModel;
    private IDispatcherTimer? timer;
    private bool visible;
    public FocusPage(FocusViewModel viewModel)
    { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); visible = true; await viewModel.RefreshCommand.ExecuteAsync();
        if (!visible) return;
        if (timer is null) { timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += async (_, _) => await viewModel.PollAsync(); }
        timer.Start();
    }
    protected override void OnDisappearing() { visible = false; timer?.Stop(); base.OnDisappearing(); }
    private async void OnWorkspaceChanged(object? sender, EventArgs e)
    { if (viewModel is not null) await viewModel.RefreshCommand.ExecuteAsync(); }
}
