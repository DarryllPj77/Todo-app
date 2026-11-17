using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public partial class GoogleEventsPage : ContentPage
{
    private readonly GoogleEventsViewModel viewModel;
    private readonly Services.ClientSession session;
    private bool selecting;
    internal Models.TaskContext? Context { get; set; }
    public GoogleEventsPage(GoogleEventsViewModel viewModel, Services.ClientSession session)
    {
        this.viewModel = viewModel; this.session = session; InitializeComponent(); BindingContext = viewModel;
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(viewModel.IsIdle) && viewModel.IsIdle) SelectContext(); };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await viewModel.RefreshCommand.ExecuteAsync(); SelectContext(); }
    private void SelectContext()
    {
        if (Context is not { } context || selecting) return;
        selecting = true;
        try
        {
            if (session.Version != context.SessionVersion)
            {
                viewModel.SelectedBlock = null; viewModel.SelectedLink = null; viewModel.Blocks.Clear(); viewModel.Links.Clear(); IsEnabled = false; return;
            }
            foreach (var block in viewModel.Blocks.Where(b => b.TaskId != context.TaskId).ToArray()) viewModel.Blocks.Remove(block);
            var ids = viewModel.Blocks.Select(b => b.Id).ToHashSet();
            foreach (var link in viewModel.Links.Where(l => !ids.Contains(l.BlockId)).ToArray()) viewModel.Links.Remove(link);
            if (viewModel.SelectedBlock is null) viewModel.SelectedBlock = viewModel.Blocks.FirstOrDefault();
        }
        finally { selecting = false; }
    }
    protected override void OnDisappearing() { viewModel.Cancel(); base.OnDisappearing(); }
}
