using System.Collections.ObjectModel;
using TodoApp.Domain.Reminders;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class RemindersViewModel : ObservableViewModel, IDisposable
{
    private readonly ReminderService reminders;
    private readonly ClientSession session;
    private bool busy;
    private string error = "";
    private ReminderTask? selectedTask;
    private ReminderRecord? selectedReminder;
    private DateTime date = DateTime.Now.AddMinutes(5).Date;
    private TimeSpan at = DateTime.Now.AddMinutes(5).TimeOfDay;
    public ObservableCollection<ReminderTask> Tasks { get; } = new();
    public ObservableCollection<ReminderRecord> Reminders { get; } = new();
    public bool IsIdle => !busy;
    public string Status => reminders.Status;
    public string PlatformInfo => reminders.DeliveryDescription;
    public string Zone => $"Time zone: {TimeZoneInfo.Local.Id}. Saved reminders keep their UTC instant when you travel.";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public DateTime Date { get => date; set => SetProperty(ref date, value); }
    public TimeSpan Time { get => at; set => SetProperty(ref at, value); }
    public ReminderTask? SelectedTask
    {
        get => selectedTask;
        set { SetProperty(ref selectedTask, value); if (value is not null) Date = value.DueDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now.AddMinutes(5).Date; Notify(); }
    }
    public ReminderRecord? SelectedReminder
    {
        get => selectedReminder;
        set
        {
            SetProperty(ref selectedReminder, value);
            if (value is not null)
            {
                SelectedTask = Tasks.FirstOrDefault(t => t.Key == value.Key);
                var local = value.FireAtUtc.ToLocalTime(); Date = local.Date; Time = local.TimeOfDay;
            }
            Notify();
        }
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand PermissionCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public RemindersViewModel(ReminderService reminders, ClientSession session)
    {
        this.reminders = reminders; this.session = session;
        RefreshCommand = Command(LoadAsync);
        PermissionCommand = Command(async () => { await reminders.RequestPermissionAsync(); await LoadAsync(); });
        RetryCommand = Command(async () => { await reminders.RefreshSafelyAsync(); await LoadAsync(); });
        SaveCommand = Command(async () =>
        {
            await reminders.SaveAsync(SelectedTask!, Date, Time, TimeZoneInfo.Local); await LoadAsync();
        }, () => SelectedTask is not null);
        CancelCommand = Command(async () => { await reminders.CancelAsync(SelectedReminder!.Key); await LoadAsync(); }, () => SelectedReminder is not null);
        reminders.Changed += StatusChanged;
        session.Changed += SessionChanged;
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (ApiClientException exception) { Error = exception.Message; }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't update reminders. Saved choices remain; retry or check device storage/settings."; }
        finally { busy = false; Notify(); }
    }, () => !busy && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the reminder request.");
    private async Task LoadAsync()
    {
        var version = session.Version;
        var tasks = await reminders.TasksAsync(); var rows = await reminders.ReadAsync(); session.EnsureVersion(version);
        SelectedTask = null; SelectedReminder = null; Tasks.Clear(); Reminders.Clear();
        foreach (var task in tasks) Tasks.Add(task);
        foreach (var row in rows) Reminders.Add(row);
        OnPropertyChanged(nameof(Zone));
    }
    private void StatusChanged() => OnPropertyChanged(nameof(Status));
    private void SessionChanged()
    {
        Tasks.Clear(); Reminders.Clear(); SelectedTask = null; SelectedReminder = null; Error = "Account changed. Refresh to select available device/account tasks.";
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(Status));
        foreach (var command in new[] { RefreshCommand, PermissionCommand, RetryCommand, SaveCommand, CancelCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() { reminders.Changed -= StatusChanged; session.Changed -= SessionChanged; }
}
