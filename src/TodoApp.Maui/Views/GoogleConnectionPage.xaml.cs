using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class GoogleConnectionPage : ContentPage
{
    private readonly GoogleConnectionViewModel viewModel;
    public GoogleConnectionPage(GoogleConnectionViewModel viewModel)
    { this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel; }
    protected override void OnDisappearing() { viewModel.Cancel(); base.OnDisappearing(); }
}
