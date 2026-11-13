using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel viewModel;

    public AccountPage(AccountViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    private async void OnServerFilterChanged(object? sender, EventArgs args) =>
        await viewModel.Workspace.RefreshCommand.ExecuteAsync();
}
