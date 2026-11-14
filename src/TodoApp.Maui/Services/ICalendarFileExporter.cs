namespace TodoApp.Maui.Services;

public interface ICalendarFileExporter
{
    Task ShareAsync(string calendar, Action ensureAccess);
}
