using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class LocationRemindersPage : ContentPage
{
    private readonly LocationRemindersViewModel viewModel;
    private Window? observedWindow;
    private readonly IDispatcherTimer timer;
    private bool visible, active, polling;
    public LocationRemindersPage(LocationRemindersViewModel viewModel)
    {
        this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel;
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(15); timer.Tick += OnTick;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); visible = active = true; observedWindow = Window;
        if (observedWindow is not null) { observedWindow.Activated += OnActivated; observedWindow.Deactivated += OnDeactivated; observedWindow.Stopped += OnDeactivated; }
        await viewModel.RefreshCommand.ExecuteAsync();
        if (visible && active) timer.Start();
    }
    protected override void OnDisappearing()
    {
        visible = active = false; timer.Stop(); viewModel.Leave();
        if (observedWindow is not null) { observedWindow.Activated -= OnActivated; observedWindow.Deactivated -= OnDeactivated; observedWindow.Stopped -= OnDeactivated; observedWindow = null; }
        base.OnDisappearing();
    }
    private void OnDeactivated(object? sender, EventArgs args) { active = false; timer.Stop(); viewModel.Cancel(); }
    private void OnActivated(object? sender, EventArgs args) { active = true; if (visible) timer.Start(); }
    private async void OnTick(object? sender, EventArgs args)
    {
        if (!visible || !active || polling) return; polling = true;
        try { await viewModel.PollNativeAsync(); }
        finally { polling = false; }
    }
}
