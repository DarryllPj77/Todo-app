using System.Collections;
using System.ComponentModel;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

// Reuses the actual feature editor/commands inside details, without starting page timers or permission flows.
internal sealed class TaskFeatureHost(IServiceProvider services, TaskContext context, Action changed) : ContentView, IDisposable
{
    private Action? leave;
    private INotifyPropertyChanged? model;
    private PropertyChangedEventHandler? listener;
    private bool applying;
    public async Task LoadAsync(string feature)
    {
        var session = services.GetRequiredService<ClientSession>();
        if (context.Workspace == HierarchyWorkspace.SyncedAccount) session.EnsureVersion(context.SessionVersion);
        switch (feature)
        {
            case "Parent":
                var hierarchy = services.GetRequiredService<HierarchyViewModel>(); hierarchy.Workspace = context.Workspace;
                await Attach(new HierarchyPage(hierarchy), hierarchy, hierarchy.RefreshCommand,
                    () => { if (hierarchy.SelectedTask?.Id != context.TaskId) hierarchy.SelectedTask = hierarchy.Rows.FirstOrDefault(t => t.Id == context.TaskId); },
                    () => hierarchy.IsIdle, new object[] { hierarchy.Rows }); break;
            case "Dependencies":
                var dependencies = services.GetRequiredService<DependenciesViewModel>(); dependencies.Workspace = context.Workspace;
                await Attach(new DependenciesPage(dependencies), dependencies, dependencies.RefreshCommand,
                    () => { if (dependencies.SelectedTask?.Id != context.TaskId) dependencies.SelectedTask = dependencies.Tasks.FirstOrDefault(t => t.Id == context.TaskId); },
                    () => dependencies.IsIdle, new object[] { dependencies.Tasks }); break;
            case "Urgency & importance":
                var planning = services.GetRequiredService<PlanningViewModel>(); planning.Workspace = context.Workspace; planning.IncludeCompleted = true;
                await Attach(new PlanningPage(planning), planning, planning.RefreshCommand,
                    () => { if (planning.SelectedTask?.Id != context.TaskId) planning.SelectedTask = planning.Tasks.FirstOrDefault(t => t.Id == context.TaskId); },
                    () => planning.IsIdle, new object[] { planning.Tasks }); break;
            case "Repeat":
                var repeat = services.GetRequiredService<RecurrenceViewModel>(); repeat.Workspace = context.Workspace;
                await Attach(new RecurrencePage(repeat), repeat, repeat.RefreshCommand,
                    () => { if (repeat.SelectedTask?.Id != context.TaskId) repeat.SelectedTask = repeat.Tasks.FirstOrDefault(t => t.Id == context.TaskId); },
                    () => repeat.IsIdle, new object[] { repeat.Tasks }); break;
            case "Reminder":
                var reminders = services.GetRequiredService<RemindersViewModel>();
                var scope = services.GetRequiredService<WorkspaceTaskReader>().Scope(context.Workspace, context.SessionVersion);
                await Attach(new RemindersPage(reminders), reminders, reminders.RefreshCommand,
                    () =>
                    {
                        if (reminders.SelectedTask?.TaskId != context.TaskId || reminders.SelectedTask?.Scope != scope)
                            reminders.SelectedTask = reminders.Tasks.FirstOrDefault(t => t.TaskId == context.TaskId && t.Scope == scope);
                        foreach (var item in reminders.Reminders.Where(r => r.TaskId != context.TaskId || r.Scope != scope).ToArray()) reminders.Reminders.Remove(item);
                    }, () => reminders.IsIdle, new object[] { reminders.Tasks }, new object[] { reminders.PermissionCommand, reminders.RetryCommand }); break;
            case "Location":
                var location = services.GetRequiredService<LocationRemindersViewModel>(); location.Workspace = context.Workspace; leave = location.Leave;
                await Attach(new LocationRemindersPage(location), location, location.RefreshCommand,
                    () =>
                    {
                        if (location.SelectedTask?.Id != context.TaskId) location.SelectedTask = location.Tasks.FirstOrDefault(t => t.Id == context.TaskId);
                        foreach (var item in location.Reminders.Where(r => r.TaskId != context.TaskId).ToArray()) location.Reminders.Remove(item);
                    }, () => location.IsIdle, new object[] { location.Tasks }, new object[] { location.PermissionCommand, location.BackgroundSettingsCommand,
                        location.StartNativeCommand, location.StopNativeCommand, location.StartBackgroundCommand, location.StopBackgroundCommand }); break;
            case "Schedule":
                var calendar = services.GetRequiredService<CalendarViewModel>(); calendar.Workspace = context.Workspace;
                await Attach(new CalendarPage(calendar), calendar, calendar.RefreshCommand,
                    () => { if (calendar.SelectedTask?.Id != context.TaskId && calendar.SelectedBlock is null) calendar.SelectedTask = calendar.Tasks.FirstOrDefault(t => t.Id == context.TaskId);
                        foreach (var item in calendar.Blocks.Where(b => b.TaskId != context.TaskId).ToArray()) calendar.Blocks.Remove(item); },
                    () => calendar.IsIdle, new object[] { calendar.Tasks }); break;
        }
    }
    private async Task Attach(ContentPage page, INotifyPropertyChanged vm, AsyncCommand refresh, Action select, Func<bool> idle, object[] hiddenSources, object[]? hiddenCommands = null)
    {
        await refresh.ExecuteAsync();
        select();
        var scroll = page.Content as ScrollView;
        View? view = scroll?.Content ?? page.Content;
        if (scroll is not null) scroll.Content = null;
        page.Content = null;
        if (view is null) return;
        // Relationship/repeat/classification pages already expose a self-contained selected-task editor.
        // Move that editor into details rather than showing their workspace-wide browser and filters.
        if (vm is HierarchyViewModel or DependenciesViewModel or PlanningViewModel or RecurrenceViewModel)
        {
            var editor = page.FindByName<Border>("TaskPropertyEditor");
            if (editor?.Parent is Layout parent)
            {
                parent.Children.Remove(editor);
                view = WorkspaceUi.Stack(WorkspaceUi.Bound("Error"), editor);
            }
        }
        if (vm is LocationRemindersViewModel && view is VerticalStackLayout places)
        {
            var configuration = places.Children.OfType<Label>().FirstOrDefault(l => l.Text?.StartsWith("Native session", StringComparison.Ordinal) == true);
            if (configuration is not null)
            {
                var first = places.Children.IndexOf(configuration);
                while (places.Children.Count > first) places.Children.RemoveAt(places.Children.Count - 1);
            }
        }
        view.BindingContext = vm; Content = view;
        foreach (var child in Descendants(view))
        {
            if (child is Picker picker && (picker.Title?.Contains("Workspace", StringComparison.OrdinalIgnoreCase) == true ||
                hiddenSources.Any(s => ReferenceEquals(s, picker.ItemsSource)))) picker.IsVisible = false;
            if (child is CollectionView collection && hiddenSources.Any(s => ReferenceEquals(s, collection.ItemsSource))) collection.IsVisible = false;
            if (child is Button button && hiddenCommands?.Any(c => ReferenceEquals(c, button.Command)) == true) button.IsVisible = false;
        }
        model = vm;
        var wasBusy = false;
        listener = (_, e) =>
        {
            if (e.PropertyName != "IsIdle" || applying) return;
            if (!idle()) { wasBusy = true; return; }
            applying = true;
            try
            {
                if (context.Workspace == HierarchyWorkspace.SyncedAccount)
                    services.GetRequiredService<ClientSession>().EnsureVersion(context.SessionVersion);
                select();
                if (wasBusy) { wasBusy = false; changed(); }
            }
            catch (ApiClientException) { IsEnabled = false; }
            finally { applying = false; }
        };
        model.PropertyChanged += listener;
    }
    private static IEnumerable<View> Descendants(IVisualTreeElement parent)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is View view) yield return view;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    public void Suspend() => leave?.Invoke();
    public void Dispose() { Suspend(); if (model is not null && listener is not null) model.PropertyChanged -= listener; }
}
