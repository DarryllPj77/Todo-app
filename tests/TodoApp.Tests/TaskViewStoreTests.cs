using Microsoft.Data.Sqlite;
using TodoApp.Domain.Views;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Tests;

public sealed class TaskViewStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_views_" + Guid.NewGuid().ToString("N"));
    private string FileName => Path.Combine(directory, "views.db3");
    private SqliteTaskViewStore Store => new(FileName);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    [Fact]
    public async Task SavedViews_ReopenUpdateRenameAndDeleteWithoutChangingIdentity()
    {
        var saved = await Store.CreateAsync("device", "  My work  ", new(" review ", Category: ViewCategory.Named, CategoryName: " Work "));
        Assert.Equal("My work", saved.Name); Assert.Equal(1, saved.Revision);
        Assert.Equal(saved, Assert.Single(await Store.ReadAsync("device")));
        var updated = await Store.UpdateAsync("device", saved.Id, saved.Revision, "Renamed", new(Completion: ViewCompletion.Pending));
        Assert.Equal(saved.Id, updated.Id); Assert.Equal(2, updated.Revision);
        Assert.Equal(updated, Assert.Single(await Store.ReadAsync("device")));
        await Store.DeleteAsync("device", updated.Id, updated.Revision);
        Assert.Empty(await Store.ReadAsync("device"));
    }
    [Fact]
    public async Task DuplicateNames_AreRejectedPerScopeAndCaseInsensitive()
    {
        var first = await Store.CreateAsync("device", "Work", new());
        await Assert.ThrowsAsync<ArgumentException>(() => Store.CreateAsync("device", " work ", new()));
        await Store.CreateAsync("account-a", "work", new());
        var second = await Store.CreateAsync("device", "Other", new());
        await Assert.ThrowsAsync<ArgumentException>(() => Store.UpdateAsync("device", second.Id, second.Revision, "WORK", new()));
        Assert.Equal(first, (await Store.ReadAsync("device")).Single(v => v.Id == first.Id));
    }
    [Fact]
    public async Task ScopeIsolation_RejectsCrossAccountUpdateAndDelete()
    {
        var saved = await Store.CreateAsync("https://one.invalid/|alice", "Private", new("secret search"));
        Assert.Empty(await Store.ReadAsync("https://one.invalid/|bob")); Assert.Empty(await Store.ReadAsync("https://two.invalid/|alice"));
        await Assert.ThrowsAsync<SavedViewConflictException>(() => Store.UpdateAsync("https://one.invalid/|bob", saved.Id, 1, "Other", new()));
        await Assert.ThrowsAsync<SavedViewConflictException>(() => Store.DeleteAsync("https://one.invalid/|bob", saved.Id, 1));
        Assert.Equal(saved, Assert.Single(await Store.ReadAsync(saved.Scope)));
    }
    [Fact]
    public async Task StaleUpdatesAndDeletes_PreserveTheNewerDefinition()
    {
        var first = await Store.CreateAsync("device", "Shared", new());
        var second = await Store.UpdateAsync("device", first.Id, first.Revision, "Shared", new("new filter"));
        await Assert.ThrowsAsync<SavedViewConflictException>(() => Store.UpdateAsync("device", first.Id, first.Revision, "Stale", new()));
        await Assert.ThrowsAsync<SavedViewConflictException>(() => Store.DeleteAsync("device", first.Id, first.Revision));
        Assert.Equal(second, Assert.Single(await Store.ReadAsync("device")));
    }
    [Fact]
    public async Task ConcurrentUpdates_HaveOneWinner()
    {
        var saved = await Store.CreateAsync("device", "Same", new());
        async Task<bool> TryUpdate(string search)
        { try { await Store.UpdateAsync("device", saved.Id, 1, "Same", new(search)); return true; } catch (SavedViewConflictException) { return false; } }
        var results = await Task.WhenAll(TryUpdate("A"), TryUpdate("B"));
        Assert.Single(results, r => r); Assert.Equal(2, Assert.Single(await Store.ReadAsync("device")).Revision);
    }
    [Fact]
    public async Task ConcurrentCreates_DoNotLoseRowsAndParametersRemainLiteral()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Store.CreateAsync("device", $"View {i}", new("' OR 1=1 --"))));
        Assert.Equal(8, (await Store.ReadAsync("device")).Count);
        Assert.All(await Store.ReadAsync("device"), v => Assert.Equal("' OR 1=1 --", v.Definition.Search));
    }
    [Fact]
    public async Task FiftyViewLimit_DoesNotBlockUpdatingAnExistingView()
    {
        var first = await Store.CreateAsync("device", "View 0", new());
        for (var i = 1; i < 50; i++) await Store.CreateAsync("device", $"View {i}", new());
        await Assert.ThrowsAsync<ArgumentException>(() => Store.CreateAsync("device", "Too many", new()));
        await Store.UpdateAsync("device", first.Id, 1, "Renamed", new("updated"));
        Assert.Equal(50, (await Store.ReadAsync("device")).Count);
    }
    [Fact]
    public async Task InvalidNamesAndFilters_DoNotWritePartialRows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.CreateAsync("device", " ", new()));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.CreateAsync("device", new string('x', 61), new()));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.CreateAsync("device", "Invalid", new(Category: ViewCategory.Named)));
        Assert.Empty(await Store.ReadAsync("device"));
    }
    [Fact]
    public async Task CorruptAndFutureSchema_FailWithoutReset()
    {
        var saved = await Store.CreateAsync("device", "Keep", new());
        using (var c = new SqliteConnection($"Data Source={FileName};Pooling=False"))
        {
            c.Open(); using var command = c.CreateCommand(); command.CommandText = "PRAGMA user_version=99;"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync("device"));
        using (var c = new SqliteConnection($"Data Source={FileName};Pooling=False"))
        {
            c.Open(); using var command = c.CreateCommand(); command.CommandText = "PRAGMA user_version=1; UPDATE SavedTaskViews SET Definition='{broken';"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => Store.ReadAsync("device"));
        using (var c = new SqliteConnection($"Data Source={FileName};Pooling=False"))
        {
            c.Open(); using var command = c.CreateCommand(); command.CommandText = "SELECT Definition FROM SavedTaskViews;";
            Assert.Equal("{broken", command.ExecuteScalar());
        }
    }
}
