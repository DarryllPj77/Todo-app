using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

// Explicit Debug-only isolation for native layout checks, never enabled in Release.
internal static class WorkspacePreview
{
    public static string? DirectoryPath { get; } = GetDirectory();
    public static bool Enabled => DirectoryPath is not null;
    private static string? GetDirectory()
    {
#if DEBUG
        var path = Environment.GetEnvironmentVariable("TODOAPP_PREVIEW_DATA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
#endif
        return null;
    }

    public static SqliteLocalTaskService CreateTasks(string root)
    {
        var service = new SqliteLocalTaskService(Path.Combine(root, "todoapp.db3"));
        if (Enabled && service.GetTasksAsync().GetAwaiter().GetResult().Count == 0)
        {
            service.CreateAsync(new TaskDetails("Prepare the weekly project review", "Review the draft, check the release notes, and write down the next steps.\n\nThis is synthetic preview data.",
                DateOnly.FromDateTime(DateTime.Today), TaskPriority.High, new Category("Work"), TagSet.Parse("planning, release"))).GetAwaiter().GetResult();
            service.CreateAsync(new TaskDetails("Read one chapter", "Keep a few notes for tomorrow.", Category: new Category("Personal"))).GetAwaiter().GetResult();
            service.CreateAsync(new TaskDetails("Capture an idea without a category")).GetAwaiter().GetResult();
        }
        return service;
    }

    public sealed class Settings : IClientSettings
    {
        public string ApiBaseUrl { get; set; } = "https://preview.invalid/";
        public bool RestorationBlocked { get; set; }
    }
    public sealed class Tokens : ISecureTokenStore
    {
        private string? value;
        public Task<string?> GetAsync() => Task.FromResult(value);
        public Task SetAsync(string token) { value = token; return Task.CompletedTask; }
        public void Clear() => value = null;
    }
    public sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("The layout preview has no network connection.");
    }
    public sealed class Notifications : IReminderPlatform
    {
        public bool SupportsScheduling => false;
        public string DeliveryDescription => "Notifications disabled in layout preview.";
        public Task<ReminderPermission> PermissionAsync(bool request) => Task.FromResult(ReminderPermission.Unavailable);
        public Task ScheduleAsync(int id, DateTimeOffset when) => Task.CompletedTask;
        public Task ShowAsync(int id) => Task.CompletedTask;
        public Task CancelAsync(int id) => Task.CompletedTask;
    }
}
