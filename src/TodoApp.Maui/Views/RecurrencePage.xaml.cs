using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class RecurrencePage : ContentPage
{
    private readonly RecurrenceViewModel viewModel;
    public RecurrencePage(RecurrenceViewModel viewModel)
    { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    protected override async void OnAppearing()
    { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); }
    private async void OnWorkspaceChanged(object? sender, EventArgs e)
    { if (viewModel is not null) await viewModel.RefreshCommand.ExecuteAsync(); }
}
