using System.Collections.ObjectModel;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class SyncWorkspaceViewModel : ObservableViewModel, IDisposable
{
    private readonly ClientSession session;
    private readonly SqliteSyncStore store;
    private readonly SyncEngine engine;
    private readonly ITaskNavigation navigation;
    private bool busy;
    private int observedVersion;
    private long editorRevision;
    private SyncTaskData? editorLocal;
    private ReplicaRow? selected;
    private string title = "", description = "", category = "", status = "Sign in through Account & server first.", error = "";
    private DateTime? due;
    private TaskPriority priority = TaskPriority.Normal;
    private bool completed;
    private string tagsInput = "";
    public ObservableCollection<ReplicaRow> Tasks { get; } = new();
    public IReadOnlyList<TaskPriority> Priorities { get; } = Enum.GetValues<TaskPriority>();
    public bool IsIdle => !busy;
    public bool CanEdit => !busy && session.Current is not null && selected?.Conflict != true;
    public bool HasConflict => selected?.Conflict == true;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Title { get => title; set => SetProperty(ref title, value ?? ""); }
    public string Description { get => description; set => SetProperty(ref description, value ?? ""); }
    public string TagsInput { get => tagsInput; set => SetProperty(ref tagsInput, value ?? ""); }
    public string CategoryName { get => category; set => SetProperty(ref category, value ?? ""); }
    public DateTime? DueDate { get => due; set => SetProperty(ref due, value); }
    public TaskPriority Priority { get => priority; set => SetProperty(ref priority, value); }
    public bool IsCompleted { get => completed; set => SetProperty(ref completed, value); }
    public string LocalVersion => selected?.Local is { } local ? $"Saved local: {local.Title}; {local.Description}; {local.Priority}; {local.DueDate}; {local.CategoryName}; {(local.IsCompleted ? "Completed" : "Pending")}; Tags: {local.Tags}" : "Saved local: deletion";
    public string ServerVersion => selected?.Remote?.Task is { } task
        ? $"Server: {task.Title}\n{task.Description}\n{task.Priority}; {task.DueDate}; {task.CategoryName}; {(task.IsCompleted ? "Completed" : "Pending")}; Tags: {task.Tags}"
        : "Server: deleted";
    public ReplicaRow? SelectedTask
    {
        get => selected;
        set
        {
            SetProperty(ref selected, value);
            editorRevision = value?.Remote?.Revision ?? 0;
            editorLocal = value?.Local;
            var task = value?.Local;
            TagsInput = task?.Tags?.ToString() ?? "";
            Title = task?.Title ?? ""; Description = task?.Description ?? ""; CategoryName = task?.CategoryName ?? "";
            DueDate = task?.DueDate?.ToDateTime(TimeOnly.MinValue); Priority = task?.Priority ?? TaskPriority.Normal;
            IsCompleted = task?.IsCompleted ?? false;
            Notify();
        }
    }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand SyncCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand KeepMineCommand { get; }
    public AsyncCommand UseServerCommand { get; }
    public AsyncCommand ClearDateCommand { get; }

    public SyncWorkspaceViewModel(ClientSession session, SqliteSyncStore store, SyncEngine engine, ITaskNavigation navigation)
    {
        observedVersion = session.Version;
        this.session = session; this.store = store; this.engine = engine; this.navigation = navigation;
        ReloadCommand = Command(() => LoadAsync());
        SyncCommand = Command(() => SynchronizeAsync(false));
        RetryCommand = Command(() => SynchronizeAsync(true));
        NewCommand = Command(() => { SelectedTask = null; return Task.CompletedTask; });
        SaveCommand = Command(SaveAsync, () => CanEdit);
        DeleteCommand = Command(DeleteAsync, () => CanEdit && SelectedTask?.Local is not null);
        KeepMineCommand = Command(() => ResolveAsync(true), () => HasConflict);
        UseServerCommand = Command(() => ResolveAsync(false), () => HasConflict);
        ClearDateCommand = Command(() => { DueDate = null; return Task.CompletedTask; }, () => CanEdit);
        session.Changed += SessionChanged;
    }

    public async Task InitializeAsync()
    {
        try { await session.RestoreAsync(); }
        catch (ApiClientException exception) { Error = exception.Message; }
        if (session.Current is not null) await ReloadCommand.ExecuteAsync();
    }

    private string Partition => session.Current is { } saved
        ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : throw new ApiClientException("Sign in to select an account cache.");
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(async () =>
    {
        busy = true; Error = ""; Notify();
        try { await action(); }
        catch (ApiClientException exception) { Error = exception.Message; }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (Exception) { Error = "Couldn't finish. Cached edits were retained; retry or check account/storage settings."; }
        finally { busy = false; Notify(); }
    }, () => !busy && session.Current is not null && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the sync action.");

    private async Task LoadAsync(bool preserveDraft = false)
    {
        var version = session.Version;
        var state = await store.ReadAsync(Partition);
        session.EnsureVersion(version);
        Status = $"{state.Rows.Values.Count(r => r.Local is not null)} cached tasks; {state.Rows.Values.Count(r => r.Pending is not null)} queued; " +
            $"{state.Rows.Values.Count(r => r.Conflict)} conflicts. Cache: {session.Current!.User.Email} at {session.Current.Origin}";
        if (preserveDraft && DraftChanged())
        {
            Status += " - Unsubmitted draft retained. Copy it before reloading if the task changed.";
            return; // Native selection changes must not clear an unsaved editor.
        }
        SelectedTask = null;
        Tasks.Clear();
        foreach (var row in state.Rows.Values.Where(r => r.Local is not null || r.Pending is not null || r.Conflict)
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id)) Tasks.Add(row);
    }
    private bool DraftChanged()
    {
        TagSet draftTags;
        try { draftTags = TagSet.Parse(TagsInput); } catch (ArgumentException) { return true; }
        var current = new SyncTaskData(Title, Description, DueDate is { } date ? DateOnly.FromDateTime(date) : null,
            Priority, CategoryName, IsCompleted, draftTags, selected?.Local?.Parent, selected?.Local?.Prerequisites, selected?.Local?.Quadrant, selected?.Local?.Recurrence);
        if (selected?.Local is not { } original)
            return Title.Length > 0 || Description.Length > 0 || CategoryName.Length > 0 || DueDate is not null ||
                Priority != TaskPriority.Normal || IsCompleted || TagsInput.Length > 0;
        return current != original with { Description = original.Description ?? "", CategoryName = original.CategoryName ?? "", Tags = original.Tags ?? TagSet.Empty };
    }

    private async Task SaveAsync()
    {
        var version = session.Version;
        await store.QueueAsync(Partition, SelectedTask?.Id ?? Guid.NewGuid(), new SyncTaskData(Title, Description,
            DueDate is { } date ? DateOnly.FromDateTime(date) : null, Priority, CategoryName, IsCompleted, TagSet.Parse(TagsInput)), SelectedTask is null ? null : editorRevision, editorLocal);
        session.EnsureVersion(version);
        await LoadAsync();
        Status += " - Saved locally. Sync sends queued changes.";
    }
    private async Task DeleteAsync()
    {
        var row = SelectedTask!;
        var partition = Partition;
        var version = session.Version;
        if (!await navigation.ConfirmDeleteAsync(row.Title)) return;
        session.EnsureVersion(version);
        await store.QueueAsync(partition, row.Id, null, editorRevision, editorLocal);
        session.EnsureVersion(version);
        await LoadAsync();
    }
    private async Task ResolveAsync(bool keepMine)
    {
        var version = session.Version;
        await store.ResolveAsync(Partition, SelectedTask!.Id, keepMine, SelectedTask.Remote?.Revision ?? 0);
        session.EnsureVersion(version);
        await LoadAsync();
    }
    private async Task SynchronizeAsync(bool retry)
    {
        var version = session.Version;
        var result = await engine.RunAsync(retry);
        session.EnsureVersion(version);
        await LoadAsync(preserveDraft: true);
        Status += " - " + result;
    }
    private void SessionChanged()
    {
        if (observedVersion == session.Version) return;
        observedVersion = session.Version;
        Tasks.Clear(); SelectedTask = null;
        Status = "Account state changed. Reload cache; sign in through Account & server if needed.";
        Error = ""; Notify();
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HasConflict)); OnPropertyChanged(nameof(ServerVersion)); OnPropertyChanged(nameof(LocalVersion));
        foreach (var command in new[] { ReloadCommand, SyncCommand, RetryCommand, NewCommand, SaveCommand,
            DeleteCommand, KeepMineCommand, UseServerCommand, ClearDateCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() => session.Changed -= SessionChanged;
}
