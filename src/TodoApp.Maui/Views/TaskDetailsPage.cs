using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public sealed class TaskDetailsPage : ContentPage
{
    private readonly IServiceProvider services;
    private readonly TaskDetailsViewModel model;
    private readonly TaskNavigationService navigation;
    private readonly List<TaskFeatureHost> hosts = new();
    private VerticalStackLayout? editors;
    public TaskDetailsPage(IServiceProvider services, TaskDetailsViewModel model, TaskNavigationService navigation)
    { this.services = services; this.model = model; this.navigation = navigation; Title = "Task details"; model.WatchSession(); }
    public async Task PrepareAsync(TaskContext context)
    {
        await model.LoadAsync(context); BindingContext = model;
        editors = WorkspaceUi.Stack(); editors.Padding = 0; editors.SetBinding(IsEnabledProperty, "CanEdit");
        editors.SetBinding(IsVisibleProperty, "CanEdit");
        foreach (var feature in new[] { "Parent", "Dependencies", "Urgency & importance", "Repeat", "Reminder", "Location", "Schedule" })
        {
            var target = feature;
            var holder = new ContentView { IsVisible = false };
            TaskFeatureHost? host = null;
            editors.Add(WorkspaceUi.Action(target, async () =>
            {
                if (host is null)
                {
                    host = new(services, context, () => Dispatcher.Dispatch(async () => await model.LoadAsync(context)));
                    hosts.Add(host); await host.LoadAsync(target); holder.Content = host;
                }
                holder.IsVisible = !holder.IsVisible;
                if (!holder.IsVisible) host.Suspend();
            }));
            editors.Add(holder);
        }
        var subtask = new Entry { Placeholder = "Add a subtask", MaxLength = 200 }; subtask.SetBinding(Entry.TextProperty, "SubtaskTitle", BindingMode.TwoWay);
        var taskHeading = WorkspaceUi.Bound("Title"); taskHeading.FontSize = 26; taskHeading.FontAttributes = FontAttributes.Bold;
        var google = WorkspaceUi.Action("Google Calendar event relationship", () => model.CanEdit ? navigation.OpenTaskGoogleEventsAsync(context) : Task.CompletedTask);
        google.IsVisible = context.Workspace == HierarchyWorkspace.SyncedAccount;
        Content = new ScrollView { Content = WorkspaceUi.Stack(taskHeading, WorkspaceUi.Bound("Summary"), WorkspaceUi.Bound("Description"),
            WorkspaceUi.Bound("Error"), WorkspaceUi.Action("Edit title, dates & priority", async () =>
            {
                if (!model.CanEdit) return;
                if (context.Workspace == HierarchyWorkspace.Device) await navigation.EditTaskAsync(context.TaskId);
                else await navigation.OpenSyncTaskAsync(context.TaskId);
            }),
            WorkspaceUi.Heading("Subtasks"), Rows("Subtasks"), subtask, WorkspaceUi.Action("Add subtask", model.AddSubtaskAsync),
            WorkspaceUi.Heading("Blocked by"), Rows("BlockedBy"), WorkspaceUi.Bound("Parent"),
            WorkspaceUi.Bound("Classification"), WorkspaceUi.Bound("Repeat"),
            WorkspaceUi.Note("Properties below save explicitly to this task. Account changes queue locally; use Account tasks to sync. Reminders, places, time blocks and focus history stay on this device."),
            editors, WorkspaceUi.Action("Focus on this task", () => model.CanEdit ? navigation.OpenTaskFocusAsync(context) : Task.CompletedTask),
            google) };
    }
    protected override async void OnAppearing() { base.OnAppearing(); if (Content is not null) await model.LoadAsync(model.Context); }
    protected override void OnDisappearing() { foreach (var host in hosts) host.Suspend(); base.OnDisappearing(); }
    private CollectionView Rows(string path)
    {
        var list = new CollectionView { HeightRequest = 130, EmptyView = "None", SelectionMode = SelectionMode.Single };
        list.SetBinding(ItemsView.ItemsSourceProperty, path);
        list.ItemTemplate = new DataTemplate(() => WorkspaceUi.Bound("Title"));
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is WorkspaceTask row) { list.SelectedItem = null; await navigation.OpenTaskDetailsAsync(row.Id, model.Context.Workspace); } };
        return list;
    }
}
