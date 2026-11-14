using System.Text;

namespace TodoApp.Maui.Services;

public sealed class CalendarFileExporter : ICalendarFileExporter
{
    public async Task ShareAsync(string calendar, Action ensureAccess)
    {
        ensureAccess();
        var directory = Path.Combine(FileSystem.CacheDirectory, "calendar-exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"todo-time-block-{Guid.NewGuid():N}.ics");
        await File.WriteAllTextAsync(path, calendar, new UTF8Encoding(false));
        try { ensureAccess(); }
        catch { File.Delete(path); throw; }
        // Sharing completes differently on each OS; retain the cache file for the recipient.
        await Share.Default.RequestAsync(new ShareFileRequest("Export time block", new ShareFile(path, "text/calendar"))
        {
            PresentationSourceBounds = DeviceInfo.Platform == DevicePlatform.iOS && DeviceInfo.Idiom == DeviceIdiom.Tablet
                ? new Rect(0, 20, 1, 1) : Rect.Zero
        });
    }
}
