namespace TodoApp.Maui.Services;

// Shared by the headless host and portable tests. Opt-out is checked directly
// before/after native show, in addition to the encrypted run/arm checks.
public sealed class BackgroundNotificationGuard(IReminderPlatform inner, Func<Task<bool>> allowed) : IReminderPlatform
{
    public bool SupportsScheduling => false;
    public string DeliveryDescription => "Generic background arrival attempt; Windows policy can prevent or delay delivery.";
    public async Task<ReminderPermission> PermissionAsync(bool request) => await allowed() ? await inner.PermissionAsync(false) : ReminderPermission.Denied;
    public Task ScheduleAsync(int id, DateTimeOffset when) => throw new NotSupportedException();
    public async Task ShowAsync(int id)
    {
        if (!await allowed()) throw new OperationCanceledException("Background run stopped before delivery.");
        await inner.ShowAsync(id);
        if (!await allowed()) { await inner.CancelAsync(id); throw new OperationCanceledException("Background run changed during delivery."); }
    }
    public Task CancelAsync(int id) => inner.CancelAsync(id);
}
