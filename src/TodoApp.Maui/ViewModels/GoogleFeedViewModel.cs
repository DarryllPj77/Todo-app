using System.Collections.ObjectModel;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class GoogleFeedViewModel : ObservableViewModel, IDisposable
{
    private readonly ClientSession session;
    private readonly SqliteCalendarStore store;
    private readonly GoogleCalendarFeedService feeds;
    private readonly ITaskNavigation navigation;
    private readonly TimeProvider clock;
    private CancellationTokenSource? operation;
    private bool busy;
    private string error = "", status = "Automatic checks are off by default. Changes only create review notices, never calendar writes or local adoption.";
    private GoogleCalendarChoice? selectedCalendar;
    private GoogleCalendarFeed? selectedFeed;
    public ObservableCollection<GoogleCalendarChoice> Calendars { get; } = new();
    public ObservableCollection<GoogleCalendarFeed> Feeds { get; } = new();
    public GoogleCalendarChoice? SelectedCalendar { get => selectedCalendar; set { SetProperty(ref selectedCalendar, value); Notify(); } }
    public GoogleCalendarFeed? SelectedFeed { get => selectedFeed; set { SetProperty(ref selectedFeed, value); OnPropertyChanged(nameof(FeedDetails)); Notify(); } }
    public string FeedDetails => SelectedFeed?.Details ?? "Select a saved monitor to inspect, scan or disable it.";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool IsIdle => !busy;
    public bool IsBusy => busy;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadCalendarsCommand { get; }
    public AsyncCommand ScanCalendarCommand { get; }
    public AsyncCommand ScanFeedCommand { get; }
    public AsyncCommand EnableCommand { get; }
    public AsyncCommand DisableCommand { get; }
    public AsyncCommand PollCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public GoogleFeedViewModel(ClientSession session, SqliteCalendarStore store, GoogleCalendarFeedService feeds, ITaskNavigation navigation, TimeProvider? clock = null)
    {
        this.session = session; this.store = store; this.feeds = feeds; this.navigation = navigation; this.clock = clock ?? TimeProvider.System;
        RefreshCommand = Command(async (scope, guard, _) => await LoadAsync(scope, guard));
        LoadCalendarsCommand = Command(async (scope, guard, ct) =>
        {
            var rows = await feeds.CalendarsAsync(scope, ct); guard(); Calendars.Clear(); foreach (var row in rows) Calendars.Add(row); SelectedCalendar = null;
            Status = "Select an owned calendar. Scans read its full collection initially but retain notices only for existing app links.";
        }, supported: true);
        ScanCalendarCommand = Command(async (scope, guard, ct) =>
        {
            var result = await feeds.ScanCalendarAsync(scope, SelectedCalendar!, ct); guard(); await LoadAsync(scope, guard); Status = Describe(result);
        }, () => SelectedCalendar is not null, true);
        ScanFeedCommand = Command(async (scope, guard, ct) =>
        {
            var result = await feeds.ScanFeedAsync(SelectedFeed!, true, ct); guard(); await LoadAsync(scope, guard); Status = Describe(result);
        }, () => SelectedFeed is not null, true);
        EnableCommand = Command(async (scope, guard, ct) =>
        {
            var calendar = SelectedCalendar!;
            if (!await navigation.ConfirmGoogleEventAsync("Enable page-open Google checks", $"While this monitor page/window is active, check {calendar.Name} ({calendar.Id}) about once per minute? Initial/reset scans read the calendar collection, discard unlinked event payloads, and flag changes only for existing app links. No event publishing, adoption or deletion is automatic. Checks stop when you leave/deactivate the page/window or disable this monitor.")) return;
            guard(); await feeds.EnableAsync(scope, calendar, ct); guard(); await LoadAsync(scope, guard);
            Status = "Read-only checks enabled for this bound Google calendar while the monitoring page/window is active. No background/OS job was scheduled.";
        }, () => SelectedCalendar is not null, true);
        DisableCommand = Command(async (scope, guard, ct) =>
        {
            await feeds.DisableAsync(SelectedFeed!, ct); guard(); await LoadAsync(scope, guard); Status = "Checks disabled locally. The cursor, notices and event links remain; Google was not contacted.";
        }, () => SelectedFeed?.Enabled == true);
        PollCommand = Command(async (scope, guard, ct) =>
        {
            var rows = await store.FeedsAsync(scope); guard();
            var attempted = 0;
            foreach (var profile in rows.Where(f => f.Enabled && (f.RetryAfterUtc is null || f.RetryAfterUtc <= this.clock.GetUtcNow()))
                .OrderBy(f => f.LastCheckedUtc ?? f.RetryAfterUtc ?? DateTimeOffset.MinValue).Take(5))
            {
                try { Status = Describe(await feeds.ScanFeedAsync(profile, false, ct)); }
                catch (Exception e) when (e is GoogleAuthorizationException or GoogleCalendarHttpException or GoogleEventConflictException or HttpRequestException or CalendarConflictException)
                { Status = "A monitor could not complete. Its prior cursor/links remain; inspect saved status/backoff and check the Google connection."; }
                attempted++; guard();
            }
            await LoadAsync(scope, guard);
            if (attempted == 0) Status = "No enabled monitor is due. Checks respect saved opt-in and retry delay.";
        }, supported: true);
        CancelCommand = new AsyncCommand(() => { Cancel(); return Task.CompletedTask; }, () => busy, _ => { });
        session.Changed += Changed;
    }
    private static string Describe(GoogleFeedResult result) => result.Skipped ? "Check skipped: no reviewable links, monitoring is off, or automatic retry is delayed. Pending work was not changed."
        : $"{(result.Reset ? "Cursor reset/full check" : result.Full ? "Full check" : "Incremental check")} completed: {result.Notices} new linked-event notices; {result.PendingSkipped} pending operations untouched. Refresh Google event links and load a review to act.";
    private string Scope()
    {
        if (session.Current is not { } saved || !session.IsVerified || saved.ExpiresAt <= clock.GetUtcNow())
        { Clear(); throw new GoogleAuthorizationException("Sign in and verify the Todo account before monitoring its Google links."); }
        return SqliteSyncStore.Partition(saved.Origin, saved.User.Id);
    }
    private AsyncCommand Command(Func<string, Action, CancellationToken, Task> action, Func<bool>? enabled = null, bool supported = false) => new(async () =>
    {
        busy = true; Error = ""; operation = new(); Notify();
        try
        {
            var scope = Scope(); var version = session.Version;
            void Guard() { operation.Token.ThrowIfCancellationRequested(); if (session.Version != version || Scope() != scope) throw new GoogleAuthorizationException("The Todo account changed. Old monitor results were discarded."); }
            await action(scope, Guard, operation.Token); Guard();
        }
        catch (Exception e) when (e is GoogleAuthorizationException or GoogleCalendarHttpException or GoogleEventConflictException or CalendarConflictException or ArgumentException) { Error = e.Message; }
        catch (OperationCanceledException) { Status = "Check canceled/timed out. No partial page cursor or automatic calendar write was committed."; }
        catch (Exception) { Error = "Couldn't finish the monitor action. Earlier cursors/links remain; refresh status and check network/storage before retrying."; }
        finally { operation.Dispose(); operation = null; busy = false; Notify(); }
    }, () => !busy && (!supported || feeds.IsSupported) && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the monitor action.");
    private async Task LoadAsync(string scope, Action guard)
    {
        var selected = SelectedFeed?.Binding; var rows = await store.FeedsAsync(scope); guard();
        Feeds.Clear(); foreach (var row in rows) Feeds.Add(row); SelectedFeed = rows.FirstOrDefault(f => selected is not null && f.Binding.Matches(selected));
    }
    private void Changed() { Cancel(); Clear(); }
    private void Clear() { Feeds.Clear(); Calendars.Clear(); SelectedFeed = null; SelectedCalendar = null; Status = "Todo account changed. Refresh the active account's monitors."; }
    public void Cancel() { try { operation?.Cancel(); } catch (ObjectDisposedException) { } }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy));
        foreach (var command in new[] { RefreshCommand, LoadCalendarsCommand, ScanCalendarCommand, ScanFeedCommand, EnableCommand, DisableCommand, PollCommand, CancelCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() { session.Changed -= Changed; Cancel(); }
}
