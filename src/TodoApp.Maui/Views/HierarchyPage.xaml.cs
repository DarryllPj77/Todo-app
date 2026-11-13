using TodoApp.Maui.ViewModels;
namespace TodoApp.Maui.Views;
public partial class HierarchyPage : ContentPage
{
    private readonly HierarchyViewModel viewModel;
    public HierarchyPage(HierarchyViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; BindingContext = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); }
    private async void OnWorkspaceChanged(object? sender, EventArgs e) { if (viewModel is not null) await viewModel.RefreshCommand.ExecuteAsync(); }
}
