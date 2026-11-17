using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class SyncPage : ContentPage
{
    private readonly SyncWorkspaceViewModel viewModel;
    private readonly IDispatcherTimer timer;
    private bool visible;
    internal Guid? InitialTaskId { get; set; }
    private async void OnTaskDetails(object? sender, EventArgs e)
    {
        if (viewModel.SelectedTask is { } row)
            await Handler!.MauiContext!.Services.GetRequiredService<Services.TaskNavigationService>().OpenTaskDetailsAsync(row.Id, HierarchyWorkspace.SyncedAccount);
    }
    private async void OnDeviceTasks(object? sender, EventArgs e) =>
        await Handler!.MauiContext!.Services.GetRequiredService<Services.TaskNavigationService>().OpenDeviceListAsync();
    private async void OnOnlineTasks(object? sender, EventArgs e) =>
        await Navigation.PushAsync(Handler!.MauiContext!.Services.GetRequiredService<ServerTasksPage>());
    public SyncPage(SyncWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += OnRetry;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        visible = true;
        await viewModel.InitializeAsync();
        if (InitialTaskId is { } id) { viewModel.SelectedTask = viewModel.Tasks.FirstOrDefault(t => t.Id == id); InitialTaskId = null; }
        if (!visible) return;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        timer.Start();
        await TrySyncAsync();
    }
    protected override void OnDisappearing()
    {
        visible = false;
        timer.Stop();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        base.OnDisappearing();
    }
    private async void OnRetry(object? sender, EventArgs e) => await TrySyncAsync();
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Dispatcher.Dispatch(async () => await TrySyncAsync());
    private async Task TrySyncAsync()
    {
        if (visible && Connectivity.Current.NetworkAccess == NetworkAccess.Internet && viewModel.SyncCommand.CanExecute(null))
            await viewModel.SyncCommand.ExecuteAsync();
    }
}
