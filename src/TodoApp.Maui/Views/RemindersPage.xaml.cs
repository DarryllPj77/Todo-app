using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class RemindersPage : ContentPage
{
    private readonly RemindersViewModel viewModel;
    public RemindersPage(RemindersViewModel viewModel)
    { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    protected override async void OnAppearing()
    { base.OnAppearing(); TimeZoneInfo.ClearCachedData(); await viewModel.RefreshCommand.ExecuteAsync(); }
}
