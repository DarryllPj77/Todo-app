using TodoApp.Maui.ViewModels;
namespace TodoApp.Maui.Views;
public partial class ServerTasksPage : ContentPage
{
    private readonly AccountViewModel model;
    public ServerTasksPage(AccountViewModel model) { this.model = model; InitializeComponent(); BindingContext = model; }
    protected override async void OnAppearing() { base.OnAppearing(); await model.InitializeAsync(); await model.Workspace.RefreshCommand.ExecuteAsync(); }
    private async void OnServerFilterChanged(object? sender, EventArgs args) => await model.Workspace.RefreshCommand.ExecuteAsync();
}
