using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public sealed class TodayPage : ContentPage
{
    private readonly TodayViewModel model;
    private readonly TaskNavigationService navigation;
    public TodayPage(TodayViewModel model, TaskNavigationService navigation)
    {
        this.model = model; this.navigation = navigation; BindingContext = model; Title = "Today"; model.WatchSession();
        var picker = new Picker { Title = "Task workspace", ItemsSource = new[] { "On this device", "My account" }, SelectedIndex = 0 };
        picker.SelectedIndexChanged += async (_, _) => { model.Workspace = picker.SelectedIndex == 1 ? HierarchyWorkspace.SyncedAccount : HierarchyWorkspace.Device; await model.RefreshAsync(); };
        Content = new ScrollView { Content = WorkspaceUi.Stack(WorkspaceUi.Heading("Today"),
            WorkspaceUi.Note("Your saved plan. Account tasks use the latest local cache; open Tasks to sync."), picker,
            WorkspaceUi.Action("Refresh today", model.RefreshAsync), WorkspaceUi.Bound("Error"),
            WorkspaceUi.Heading("Overdue"), TaskList("Overdue"), WorkspaceUi.Heading("Due today"), TaskList("Due"),
            WorkspaceUi.Heading("Scheduled"), ReadList("Scheduled", "Title", "Summary"),
            WorkspaceUi.Heading("Reminders today"), ReadList("Reminders", "Title", "Summary")) };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.RefreshAsync(); }
    private CollectionView TaskList(string path)
    {
        var list = ReadList(path, "Title", "Summary"); list.SelectionMode = SelectionMode.Single;
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is WorkspaceTask row) { list.SelectedItem = null; await navigation.OpenTaskDetailsAsync(row.Id, model.Workspace); } };
        return list;
    }
    private static CollectionView ReadList(string path, string title, string detail)
    {
        var list = new CollectionView { HeightRequest = 160, EmptyView = "Nothing scheduled here." };
        list.SetBinding(ItemsView.ItemsSourceProperty, path);
        list.ItemTemplate = new DataTemplate(() => { var stack = WorkspaceUi.Stack(WorkspaceUi.Bound(title), WorkspaceUi.Bound(detail)); stack.Padding = 8; return stack; });
        return list;
    }
}
