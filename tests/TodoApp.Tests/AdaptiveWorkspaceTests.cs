using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class AdaptiveWorkspaceTests
{
    [Fact]
    public async Task CategoriesSeparateVirtualGroupsAndCombineWithLiteralSearch()
    {
        var service = new InMemoryLocalTaskService();
        await service.CreateAsync(new TaskDetails("Work [draft]", "Review notes", Category: new Category("Work"), Tags: TagSet.Parse("release")));
        await service.CreateAsync(new TaskDetails("Second", Category: new Category("work")));
        await service.CreateAsync(new TaskDetails("Virtual name", Category: new Category("All categories")));
        await service.CreateAsync(new TaskDetails("Loose"));
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();

        Assert.Equal(4, vm.Categories.Count);
        Assert.Equal(4, vm.Categories.Single(c => c.IsAll).Count);
        vm.SelectedCategory = vm.Categories.Single(c => string.Equals(c.Name, "Work", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, vm.Tasks.Count);
        vm.SearchText = " RELEASE ";
        Assert.Equal("Work [draft]", Assert.Single(vm.Tasks).Title);
        vm.SearchText = "[draft]";
        Assert.Single(vm.Tasks);
        vm.SearchText = ".*";
        Assert.Empty(vm.Tasks);
        Assert.Equal(2, vm.SelectedCategory.Count);
        vm.SearchText = "";
        vm.SelectedCategory = vm.Categories.Single(c => !c.IsAll && c.Name is null);
        Assert.Equal("Loose", Assert.Single(vm.Tasks).Title);
        vm.SelectedCategory = vm.Categories.Single(c => c.Name == "All categories");
        Assert.Equal("Virtual name", Assert.Single(vm.Tasks).Title);
    }

    [Fact]
    public async Task ResizePreservesSelectionAndDoesNotModifyTasks()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Keep selected", "Long notes"));
        var original = task.UpdatedAt;
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks);
        await vm.ShowDetailsCommand.ExecuteAsync();
        Assert.False(vm.IsTaskPaneVisible);
        vm.SetViewportWidth(1180);
        Assert.True(vm.IsWideLayout);
        Assert.True(vm.IsTaskPaneVisible);
        Assert.True(vm.IsDetailsPaneVisible);
        vm.SetViewportWidth(1179);
        Assert.True(vm.IsCompactLayout);
        Assert.False(vm.IsTaskPaneVisible);
        Assert.Equal(task.Id, vm.SelectedTask?.Id);
        await vm.ShowTasksCommand.ExecuteAsync();
        Assert.True(vm.IsTaskPaneVisible);
        Assert.False(vm.IsDetailsPaneVisible);
        Assert.Equal(original, task.UpdatedAt);
        Assert.Equal("Long notes", vm.SelectedTask?.Description);
    }

    [Fact]
    public async Task RefreshRetainsVisibleSelectionAndReadsUpdatedDetails()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Original", Category: new Category("Work")));
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();
        vm.SelectedCategory = vm.Categories.Single(c => c.Name == "Work");
        vm.SelectedTask = Assert.Single(vm.Tasks);
        await vm.ShowDetailsCommand.ExecuteAsync();
        await service.UpdateAsync(task.Id, new TaskDetails("Updated", "Changed notes", Category: task.Category));
        await vm.RefreshAsync();
        Assert.Equal("Updated", vm.DetailsHeading);
        Assert.Equal("Changed notes", vm.SelectedTask?.Description);
        Assert.True(vm.IsDetailsPaneVisible);
        Assert.Equal("Work", vm.SelectedCategory?.Name);
    }

    [Fact]
    public async Task FilteredOrDeletedSelectionCannotLeaveAnActionableDetailPane()
    {
        var service = new InMemoryLocalTaskService();
        var task = await service.CreateAsync(new TaskDetails("Selected"));
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks);
        await vm.ShowDetailsCommand.ExecuteAsync();
        vm.SearchText = "Does not match";
        Assert.Null(vm.SelectedTask);
        Assert.False(vm.IsDetailsPaneVisible);
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.ShowDetailsCommand.CanExecute(null));
        vm.SearchText = "";
        vm.SelectedTask = Assert.Single(vm.Tasks);
        await service.DeleteAsync(task.Id);
        await vm.RefreshAsync();
        Assert.Null(vm.SelectedTask);
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task FilterCountsAndMissingCategoryFallbackFollowLatestRead()
    {
        var service = new InMemoryLocalTaskService();
        var done = await service.CreateAsync(new TaskDetails("Done", Category: new Category("Work")));
        await service.SetCompletedAsync(done.Id, true);
        await service.CreateAsync(new TaskDetails("Pending"));
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();
        vm.SelectedCategory = vm.Categories.Single(c => c.Name == "Work");
        vm.SelectedFilter = vm.Filters.Single(f => f.Value == TaskFilter.Pending);
        await vm.RefreshAsync();
        Assert.True(vm.SelectedCategory?.IsAll);
        Assert.Equal(1, vm.Categories[0].Count);
        Assert.Equal("Pending", Assert.Single(vm.Tasks).Title);
    }

    [Fact]
    public async Task DisplayFallbacksAreNotSearchableTaskData()
    {
        var service = new InMemoryLocalTaskService();
        await service.CreateAsync(new TaskDetails("One"));
        var vm = new TaskListViewModel(service, new ClientNavigation());
        await vm.RefreshAsync();
        vm.SearchText = "No description";
        Assert.Empty(vm.Tasks);
        vm.SearchText = "category:";
        Assert.Empty(vm.Tasks);
    }
}
