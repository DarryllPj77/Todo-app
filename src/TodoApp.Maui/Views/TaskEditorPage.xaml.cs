using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class TaskEditorPage : ContentPage
{
    public TaskEditorViewModel ViewModel { get; }

    public TaskEditorPage(TaskEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
    }
}
