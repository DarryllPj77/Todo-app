using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel viewModel;
    public CalendarPage(CalendarViewModel viewModel)
    { this.viewModel = viewModel; InitializeComponent(); BindingContext = viewModel;
        ToolbarItems.Add(new ToolbarItem { Text = "Linked events", Command = new Command(async () => await Navigation.PushAsync(Handler!.MauiContext!.Services.GetRequiredService<GoogleEventsPage>())) }); }
    protected override async void OnAppearing()
    { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); }
    private async void OnWorkspaceChanged(object? sender, EventArgs args)
    { if (Window is not null) await viewModel.RefreshCommand.ExecuteAsync(); }
}
