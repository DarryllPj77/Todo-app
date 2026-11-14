using System.Collections.ObjectModel;
using System.Globalization;
using TodoApp.Domain.Location;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class LocationRemindersViewModel : ObservableViewModel, IDisposable
{
    private readonly LocationReminderService service;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private CancellationTokenSource lifetime = new();
    private int generation;
    private bool busy;
    private HierarchyWorkspace workspace;
    private LocationTask? task;
    private LocationReminder? selected;
    private string place = "", latitude = "", longitude = "", radius = "200", error = "", status = "Refresh to load saved places.";
    public ObservableCollection<LocationTask> Tasks { get; } = new();
    public ObservableCollection<LocationReminder> Reminders { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public bool IsIdle => !busy;
    public LocationTask? SelectedTask { get => task; set => SetProperty(ref task, value); }
    public LocationReminder? SelectedReminder
    {
        get => selected;
        set
        {
            if (!SetProperty(ref selected, value)) return;
            if (value is not null)
            {
                SelectedTask = Tasks.FirstOrDefault(t => t.Id == value.TaskId); PlaceName = value.PlaceName;
                Latitude = value.Latitude.ToString("G17", CultureInfo.InvariantCulture); Longitude = value.Longitude.ToString("G17", CultureInfo.InvariantCulture);
                Radius = value.RadiusMeters.ToString(CultureInfo.InvariantCulture);
            }
            OnPropertyChanged(nameof(Details)); Notify();
        }
    }
    public string Details => SelectedReminder?.Status ?? "Choose a saved reminder to edit, disable or delete; choose New for another place.";
    public string PlaceName { get => place; set => SetProperty(ref place, value ?? ""); }
    public string Latitude { get => latitude; set => SetProperty(ref latitude, value ?? ""); }
    public string Longitude { get => longitude; set => SetProperty(ref longitude, value ?? ""); }
    public string Radius { get => radius; set => SetProperty(ref radius, value ?? ""); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand PositionCommand { get; }
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand PermissionCommand { get; }
    public AsyncCommand DisableCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public LocationRemindersViewModel(LocationReminderService service, ClientSession session, ITaskNavigation navigation)
    {
        this.service = service; this.session = session; this.navigation = navigation;
        RefreshCommand = Command(RefreshAsync);
        SaveCommand = Command(SaveAsync);
        NewCommand = Command(() => { ResetDraft(); return Task.CompletedTask; });
        PositionCommand = Command(PositionAsync);
        CheckCommand = Command(CheckAsync);
        PermissionCommand = Command(PermissionAsync);
        DisableCommand = Command(() => RemoveAsync(false), () => SelectedReminder is not null);
        DeleteCommand = Command(() => RemoveAsync(true), () => SelectedReminder is not null);
        CancelCommand = new(() => { Cancel(); Status = "Current check canceled. Refresh to review saved state."; return Task.CompletedTask; }, () => true, _ => { });
        session.Changed += OnSessionChanged;
    }
    private sealed record Context(string Scope, int Generation, int Version, CancellationToken Token);
    private Context Capture()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a location workspace.");
        var scope = Workspace == HierarchyWorkspace.Device ? "device" : session.Current is { } saved
            ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in to manage this account's local places.");
        return new(scope, generation, session.Version, lifetime.Token);
    }
    private void Ensure(Context context)
    {
        context.Token.ThrowIfCancellationRequested();
        if (context.Generation != generation) throw new OperationCanceledException();
        session.EnsureVersion(context.Version);
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify(); var stamp = generation;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or ApiClientException)
        { if (stamp == generation) Error = exception.Message; }
        catch (Exception) { if (stamp == generation) Error = "Location action could not finish. No data was reset. Refresh to review saved state; delivery is not guaranteed."; }
        finally { busy = false; Notify(); }
    }, () => !busy && (enabled?.Invoke() ?? true), _ => Error = "Location action could not finish.");
    private async Task RefreshAsync()
    {
        var context = Capture(); var snapshot = await service.ReadAsync(context.Scope, context.Token); Ensure(context);
        var id = SelectedReminder?.Id; Tasks.Clear(); foreach (var item in snapshot.Tasks) Tasks.Add(item);
        Reminders.Clear(); foreach (var item in snapshot.Reminders) Reminders.Add(item);
        SelectedReminder = Reminders.FirstOrDefault(r => r.Id == id);
        if (SelectedTask is not null) SelectedTask = Tasks.FirstOrDefault(t => t.Id == SelectedTask.Id);
        Status = "Places are local to this device and workspace. Arrival checks run only when you press Check arrivals now.";
    }
    private async Task SaveAsync()
    {
        var context = Capture(); var prior = SelectedReminder;
        static double Number(string value) => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN;
        var draft = new LocationReminderDraft(SelectedTask?.Id ?? Guid.Empty, PlaceName, Number(Latitude), Number(Longitude),
            int.TryParse(Radius.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var meters) ? meters : 0);
        LocationRules.Validate(draft);
        if (!await navigation.ConfirmLocationActionAsync("Save and arm place", "Store this task and place encrypted on this device? Each arm requires an outside check followed by an inside check and permits one generic notification attempt. No background monitoring or upload occurs.")) return;
        Ensure(context); await service.ArmAsync(context.Scope, prior, draft, context.Token); Ensure(context);
        ResetDraft(); await RefreshAsync();
    }
    private async Task PositionAsync()
    {
        var context = Capture();
        if (!await navigation.ConfirmLocationActionAsync("Use current location", "Request one foreground location fix to fill the coordinate fields? The OS may ask for location access. The sample is not saved until you confirm Save and arm; no location history is collected.")) return;
        Ensure(context); var fix = await service.PositionAsync(context.Scope, context.Token); Ensure(context);
        Latitude = fix.Latitude.ToString("F6", CultureInfo.InvariantCulture); Longitude = fix.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        Status = "Coordinates filled from one location fix. Review the place and radius before saving.";
    }
    private async Task CheckAsync()
    {
        var context = Capture();
        if (!await navigation.ConfirmLocationActionAsync("Check arrivals now", "Request one foreground location fix and compare armed places in this workspace? The OS may ask for location access. A confirmed arrival may show one generic task notification. No sample/history is stored or uploaded.")) return;
        Ensure(context); var result = await service.CheckAsync(context.Scope, context.Token); Ensure(context);
        await RefreshAsync(); Ensure(context); Status = result;
    }
    private async Task PermissionAsync()
    {
        var context = Capture();
        if (!await navigation.ConfirmLocationActionAsync("Allow notifications", "Allow generic task alerts? The OS may ask for notification access. Task titles, place names and coordinates are not included in notifications.")) return;
        Ensure(context); var result = await service.NotificationPermissionAsync(context.Scope, context.Token); Ensure(context);
        Status = "Notification permission: " + result + ". Device settings, focus modes and OS policy can still prevent delivery.";
    }
    private async Task RemoveAsync(bool delete)
    {
        var context = Capture(); var row = SelectedReminder ?? throw new ArgumentException("Select a saved reminder.");
        if (!await navigation.ConfirmLocationActionAsync(delete ? "Delete saved place" : "Disable reminder", delete ? "Delete this local place/reminder and cancel its generic notification? The task remains saved." : "Disable this location reminder and cancel its generic notification?")) return;
        Ensure(context); await service.RemoveAsync(context.Scope, row, delete, context.Token); Ensure(context); ResetDraft(); await RefreshAsync();
    }
    private void ResetDraft() { SelectedReminder = null; SelectedTask = null; PlaceName = Latitude = Longitude = ""; Radius = "200"; }
    public void Cancel() { generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new(); }
    private void Clear() { Cancel(); Tasks.Clear(); Reminders.Clear(); ResetDraft(); Status = "Workspace changed. Refresh to load saved places."; Error = ""; Notify(); }
    private void OnSessionChanged() => Clear();
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle));
        foreach (var command in new[] { RefreshCommand, SaveCommand, NewCommand, PositionCommand, CheckCommand, PermissionCommand, DisableCommand, DeleteCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Leave() { Cancel(); ResetDraft(); }
    public void Dispose() { session.Changed -= OnSessionChanged; lifetime.Cancel(); lifetime.Dispose(); }
}
