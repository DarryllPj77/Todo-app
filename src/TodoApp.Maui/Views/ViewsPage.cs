using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Views;

public sealed class ViewsPage : ContentPage
{
    public ViewsPage(IServiceProvider services, TaskNavigationService navigation)
    {
        Title = "Views";
        var content = WorkspaceUi.Stack(WorkspaceUi.Heading("Find your next task"), WorkspaceUi.Note("Smart filters and your saved views. Each view stays in its chosen workspace."));
        foreach (var filter in Enum.GetValues<TodoApp.Domain.Enums.TaskFilter>())
        {
            var option = filter;
            content.Add(WorkspaceUi.Action(option.ToString() switch { "DueToday" => "Due today", "HighPriority" => "High priority", _ => option.ToString() }, async () =>
            {
                var page = services.GetRequiredService<TaskListPage>();
                var vm = (TaskListViewModel)page.BindingContext; vm.SelectedFilter = vm.Filters.First(f => f.Value == option);
                await Navigation.PushAsync(page);
            }));
        }
        content.Add(WorkspaceUi.Action("Saved views · device & account", navigation.OpenTaskViewsAsync));
        content.Add(WorkspaceUi.Action("Eisenhower matrix", navigation.OpenPlanningAsync));
        Content = new ScrollView { Content = content };
    }
}
