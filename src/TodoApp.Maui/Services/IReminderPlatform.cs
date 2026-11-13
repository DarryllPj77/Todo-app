namespace TodoApp.Maui.Services;

public enum ReminderPermission { Allowed, Denied, Unavailable }
public interface IReminderPlatform
{
    bool SupportsScheduling { get; }
    string DeliveryDescription { get; }
    Task<ReminderPermission> PermissionAsync(bool request);
    Task ScheduleAsync(int id, DateTimeOffset when);
    Task ShowAsync(int id);
    Task CancelAsync(int id);
}
