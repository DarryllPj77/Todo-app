using System.Collections.ObjectModel;
using System.Globalization;
using TodoApp.Domain.Calendar;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class CalendarViewModel : ObservableViewModel, IDisposable
{
    private readonly ILocalTaskService device;
    private readonly SqliteSyncStore sync;
    private readonly SqliteCalendarStore store;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private readonly ICalendarFileExporter exporter;
    private readonly TimeProvider clock;
    private bool busy, dirty;
    private int generation;
    private HierarchyWorkspace workspace;
    private TimeBlock? selectedBlock;
    private CalendarTask? selectedTask;
    private Guid draftId = Guid.NewGuid();
    private DateTime day = DateTime.Today;
    private TimeSpan startTime = new(9, 0, 0);
    private string duration = "30", zoneId = TimeZoneInfo.Local.Id, error = "";
    private string status = "Plan a fixed block of time. Google consent is available through Tools; event synchronization comes in the next increment.";

    public ObservableCollection<CalendarTask> Tasks { get; } = new();
    public ObservableCollection<TimeBlock> Blocks { get; } = new();
    public IReadOnlyList<HierarchyWorkspace> Workspaces { get; } = Enum.GetValues<HierarchyWorkspace>();
    public HierarchyWorkspace Workspace { get => workspace; set { if (SetProperty(ref workspace, value)) Clear(); } }
    public CalendarTask? SelectedTask { get => selectedTask; set { if (SetProperty(ref selectedTask, value)) dirty = true; Notify(); } }
    public TimeBlock? SelectedBlock
    {
        get => selectedBlock;
        set
        {
            SetProperty(ref selectedBlock, value);
            if (value is not null)
            {
                draftId = value.Id; Day = value.WallStart.Date; StartTime = value.WallStart.TimeOfDay;
                Duration = ((int)(value.EndUtc - value.StartUtc).TotalMinutes).ToString(CultureInfo.InvariantCulture);
                zoneId = value.TimeZoneId; SelectedTask = Tasks.FirstOrDefault(t => t.Id == value.TaskId);
            }
            else ResetDraft();
            dirty = false; Notify();
        }
    }
    public DateTime Day { get => day; set { if (SetProperty(ref day, value)) dirty = true; Notify(); } }
    public TimeSpan StartTime { get => startTime; set { if (SetProperty(ref startTime, value)) dirty = true; Notify(); } }
    public string Duration { get => duration; set { if (SetProperty(ref duration, value)) dirty = true; Notify(); } }
    public string ZoneLabel => $"Time zone: {zoneId}. Duration measures elapsed minutes.";
    public string DraftTitle => SelectedBlock?.Title ?? SelectedTask?.Title ?? "Choose a pending task";
    public bool IsIdle => !busy;
    public bool CanSelectBlock => !busy && !dirty;
    public bool IsNewBlock => SelectedBlock is null;
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand ExportCommand { get; }

    public CalendarViewModel(ILocalTaskService device, SqliteSyncStore sync, SqliteCalendarStore store,
        ClientSession session, ITaskNavigation navigation, ICalendarFileExporter exporter, TimeProvider? clock = null)
    {
        this.device = device; this.sync = sync; this.store = store; this.session = session;
        this.navigation = navigation; this.exporter = exporter; this.clock = clock ?? TimeProvider.System;
        RefreshCommand = Command(RefreshAsync);
        NewCommand = Command(NewAsync);
        SaveCommand = Command(SaveAsync, () => SelectedBlock is not null || SelectedTask is not null);
        DeleteCommand = Command(DeleteAsync, () => SelectedBlock is not null);
        ExportCommand = Command(ExportAsync, () => SelectedBlock is not null);
        session.Changed += SessionChanged;
    }

    private sealed record Context(string Scope, int Generation, int Version);
    private Context Capture()
    {
        if (!Enum.IsDefined(Workspace)) throw new ArgumentException("Choose a calendar workspace.");
        if (Workspace == HierarchyWorkspace.Device) return new("device", generation, session.Version);
        var saved = session.Current;
        if (saved is null || saved.ExpiresAt <= clock.GetUtcNow()) { Clear(); throw new ApiClientException("Sign in again to open account time blocks."); }
        return new(SqliteSyncStore.Partition(saved.Origin, saved.User.Id), generation, session.Version);
    }
    private void Ensure(Context context)
    {
        if (context.Generation != generation) throw new ApiClientException("Workspace changed; old calendar results were discarded.");
        if (context.Scope == "device") return;
        if (session.Version != context.Version || session.Current is not { } saved || saved.ExpiresAt <= clock.GetUtcNow()
            || SqliteSyncStore.Partition(saved.Origin, saved.User.Id) != context.Scope)
        { Clear(); throw new ApiClientException("The account changed or expired. Sign in and refresh."); }
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (Exception exception) when (exception is ArgumentException or ApiClientException or CalendarConflictException)
        { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't finish the calendar action. Saved blocks remain; refresh and review before retrying."; }
        finally { busy = false; Notify(); }
    }, () => !busy && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the calendar action.");

    private async Task<IReadOnlyList<CalendarTask>> ReadTasksAsync(Context context)
    {
        IReadOnlyList<CalendarTask> rows;
        if (context.Scope == "device") rows = (await device.GetTasksAsync()).Where(t => !t.IsCompleted).Select(t => new CalendarTask(t.Id, t.Title)).ToArray();
        else
        {
            var replica = await sync.ReadAsync(context.Scope); Ensure(context);
            rows = replica.Rows.Values.Where(r => r.Local is { IsCompleted: false } && !r.Conflict)
                .Select(r => new CalendarTask(r.Id, r.Local!.Title)).ToArray();
        }
        Ensure(context); return rows.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id).ToArray();
    }
    private async Task LoadAsync(Context context, Guid? selectId = null)
    {
        var tasks = await ReadTasksAsync(context); var blocks = await store.ReadAsync(context.Scope); Ensure(context);
        var selectedId = selectId ?? SelectedBlock?.Id;
        Tasks.Clear(); foreach (var task in tasks) Tasks.Add(task);
        Blocks.Clear(); foreach (var block in blocks) Blocks.Add(block);
        SelectedBlock = Blocks.FirstOrDefault(b => b.Id == selectedId);
        if (SelectedBlock is null) ResetDraft();
        Status = $"{Blocks.Count} local blocks. Overlaps are rejected within this workspace. Imported copies do not stay in sync.";
    }
    private async Task RefreshAsync()
    {
        var context = Capture();
        if (dirty && !await navigation.ConfirmDiscardCalendarDraftAsync()) return;
        Ensure(context); await LoadAsync(context);
    }
    private async Task NewAsync()
    {
        var context = Capture();
        if (dirty && !await navigation.ConfirmDiscardCalendarDraftAsync()) return;
        Ensure(context); SelectedBlock = null; ResetDraft();
    }
    private void ResetDraft()
    {
        draftId = Guid.NewGuid(); SelectedTask = null; Day = clock.GetLocalNow().Date; StartTime = new(9, 0, 0);
        Duration = "30"; zoneId = TimeZoneInfo.Local.Id; dirty = false; Notify();
    }
    private async Task SaveAsync()
    {
        var context = Capture(); var block = SelectedBlock; var id = draftId;
        var taskId = block?.TaskId ?? SelectedTask!.Id;
        var title = block?.Title;
        if (block is null)
        {
            var task = (await ReadTasksAsync(context)).SingleOrDefault(t => t.Id == taskId)
                ?? throw new ArgumentException("The task is missing, completed or conflicted. Refresh and choose a pending task.");
            title = task.Title;
        }
        if (!int.TryParse(Duration, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            throw new ArgumentException("Use a duration of 1-1440 whole minutes.");
        if (StartTime < TimeSpan.Zero || StartTime >= TimeSpan.FromDays(1)) throw new ArgumentException("Choose a valid start time.");
        var wall = DateTime.SpecifyKind(Day.Date.Add(StartTime), DateTimeKind.Unspecified);
        Ensure(context);
        var saved = await store.SaveAsync(context.Scope, id, new(taskId, title!, wall, zoneId, minutes), block?.Revision);
        Ensure(context); SelectedBlock = saved; dirty = false;
        await LoadAsync(context, saved.Id);
    }
    private async Task DeleteAsync()
    {
        var context = Capture(); var block = SelectedBlock!;
        if (!await navigation.ConfirmDeleteCalendarBlockAsync(block.Title)) return;
        Ensure(context); await store.DeleteAsync(context.Scope, block.Id, block.Revision); Ensure(context);
        SelectedBlock = null; await LoadAsync(context);
    }
    private async Task ExportAsync()
    {
        var context = Capture(); var block = SelectedBlock!;
        if (dirty) throw new ArgumentException("Save or refresh the draft before exporting the saved block.");
        var latest = (await store.ReadAsync(context.Scope)).SingleOrDefault(b => b.Id == block.Id); Ensure(context);
        if (latest?.Revision != block.Revision) throw new CalendarConflictException();
        var calendar = CalendarFile.Export([latest]);
        await exporter.ShareAsync(calendar, () => Ensure(context)); Ensure(context);
        Status = "Share requested for one saved block. Import it once into a calendar you choose; this does not create a live connection.";
    }
    private void SessionChanged() { if (Workspace == HierarchyWorkspace.SyncedAccount) Clear(); }
    private void Clear()
    {
        generation++; Tasks.Clear(); Blocks.Clear(); SelectedBlock = null; ResetDraft(); Error = "";
        Status = "Workspace/account changed. Refresh to load its local blocks.";
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanSelectBlock)); OnPropertyChanged(nameof(IsNewBlock)); OnPropertyChanged(nameof(ZoneLabel)); OnPropertyChanged(nameof(DraftTitle));
        foreach (var command in new[] { RefreshCommand, NewCommand, SaveCommand, DeleteCommand, ExportCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
