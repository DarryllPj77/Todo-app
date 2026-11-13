using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed class ReminderRuntime(ReminderService reminders, ClientSession session)
{
    private IDispatcherTimer? timer;
    private int windows;
    private bool subscribed;
    public async Task AttachAsync(IDispatcher dispatcher)
    {
        windows++;
        if (!subscribed) { session.Changed += SessionChanged; subscribed = true; }
        try { await session.RestoreAsync(); } catch (Exception) { /* Account page offers session/settings recovery; task startup remains usable. */ }
        MirrorScope();
        if (windows == 0) return;
        if (timer is null)
        {
            timer = dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(15);
            timer.Tick += async (_, _) => await reminders.RefreshSafelyAsync();
        }
        timer.Start();
        await reminders.RefreshSafelyAsync();
    }
    public void Detach()
    {
        if (--windows <= 0) { windows = 0; timer?.Stop(); }
    }
    public Task ResumeAsync() { TimeZoneInfo.ClearCachedData(); return reminders.RefreshSafelyAsync(); }
    private void MirrorScope()
    {
        try
        {
            var scope = session.Current is { } saved ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : "";
            Preferences.Default.Set("todoapp.reminder-active-scope", scope);
        }
        catch (Exception) { /* Reconciliation still cancels inactive scopes; payloads contain no account/task details. */ }
    }
    private async void SessionChanged()
    {
        MirrorScope();
        await reminders.RefreshSafelyAsync();
    }
}
