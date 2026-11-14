using System.Collections.ObjectModel;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class GoogleEventsViewModel : ObservableViewModel, IDisposable
{
    private readonly ClientSession session;
    private readonly SqliteCalendarStore store;
    private readonly GoogleEventLinkService links;
    private readonly ITaskNavigation navigation;
    private readonly TimeProvider clock;
    private CancellationTokenSource? operation;
    private bool busy;
    private string error = "", status = "Refresh local blocks/links, then load owned Google calendars explicitly.";
    private TimeBlock? block;
    private GoogleEventLink? link;
    private GoogleCalendarChoice? calendar;
    public ObservableCollection<TimeBlock> Blocks { get; } = new();
    public ObservableCollection<GoogleEventLink> Links { get; } = new();
    public ObservableCollection<GoogleCalendarChoice> Calendars { get; } = new();
    public TimeBlock? SelectedBlock { get => block; set { SetProperty(ref block, value); Notify(); } }
    public GoogleEventLink? SelectedLink { get => link; set { SetProperty(ref link, value); OnPropertyChanged(nameof(LinkDetails)); OnPropertyChanged(nameof(ReviewDetails)); Notify(); } }
    public GoogleCalendarChoice? SelectedCalendar { get => calendar; set { SetProperty(ref calendar, value); Notify(); } }
    public string LinkDetails => SelectedLink?.Details ?? "Select a link to inspect its state. Pending retries use the original saved operation.";
    public string ReviewDetails => SelectedLink?.Review?.Details ?? "Load a Google/local review before resolving a difference. Review alone changes no block or provider event.";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool IsIdle => !busy;
    public bool IsBusy => busy;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadCalendarsCommand { get; }
    public AsyncCommand PublishCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand ReviewCommand { get; }
    public AsyncCommand UseGoogleCommand { get; }
    public AsyncCommand KeepLocalCommand { get; }
    public AsyncCommand AcceptMissingCommand { get; }
    public AsyncCommand DetachCommand { get; }
    public GoogleEventsViewModel(ClientSession session, SqliteCalendarStore store, GoogleEventLinkService links, ITaskNavigation navigation, TimeProvider? clock = null)
    {
        this.session = session; this.store = store; this.links = links; this.navigation = navigation; this.clock = clock ?? TimeProvider.System;
        RefreshCommand = Command(async (scope, guard, _) => await LoadAsync(scope, guard));
        LoadCalendarsCommand = Command(async (scope, guard, ct) =>
        {
            var choices = await links.CalendarsAsync(scope, ct); guard();
            Calendars.Clear(); foreach (var choice in choices) Calendars.Add(choice); SelectedCalendar = null;
            Status = $"{choices.Count} owned calendars loaded. Select the destination before publishing.";
        }, supported: true);
        PublishCommand = Command(async (scope, guard, ct) =>
        {
            var source = SelectedBlock!; var destination = SelectedCalendar!;
            var prior = Links.SingleOrDefault(l => l.BlockId == source.Id);
            if (!await navigation.ConfirmGoogleEventAsync("Publish local block", $"Publish {source.Title} from {source.StartUtc:u} to {source.EndUtc:u} into {destination.Name} ({destination.Id})? This sends the captured title and times to Google. Existing linked events are conditionally updated.")) return;
            guard(); var result = await links.PublishAsync(scope, source, destination, prior?.Revision, ct); guard();
            await LoadAsync(scope, guard); Status = result.State == GoogleLinkState.Conflict ? result.Problem : "The local block was published to its Google event. Later edits require an explicit push.";
        }, () => SelectedBlock is not null && SelectedCalendar is not null, supported: true);
        DeleteCommand = Command(async (scope, guard, ct) =>
        {
            var selected = SelectedLink!;
            if (!await navigation.ConfirmGoogleEventAsync("Delete linked Google event", $"Delete only the linked event for {selected.Desired.Title} from {selected.CalendarName} ({selected.CalendarId})? The local block/task and imported calendar copies remain.")) return;
            guard(); var result = await links.DeleteAsync(selected, ct); guard(); await LoadAsync(scope, guard);
            Status = result.State == GoogleLinkState.Conflict ? result.Problem : "Linked Google event deleted/confirmed absent. Local block and task remain.";
        }, () => SelectedLink?.State == GoogleLinkState.Linked, supported: true);
        RetryCommand = Command(async (scope, guard, ct) =>
        {
            var selected = SelectedLink!;
            if (!await navigation.ConfirmGoogleEventAsync("Retry saved Google operation", $"Retry {selected.State} for {selected.Desired.Title} from {selected.Desired.StartUtc:u} to {selected.Desired.EndUtc:u} in {selected.CalendarName}? This may finish a previously confirmed create, update or deletion using its captured snapshot.")) return;
            guard(); var result = await links.RetryAsync(selected, ct); guard(); await LoadAsync(scope, guard);
            Status = result.State == GoogleLinkState.Conflict ? result.Problem : "Saved operation reconciled. Refresh the local block before pushing newer edits.";
        }, () => SelectedLink?.Pending == true, supported: true);
        ReviewCommand = Command(async (scope, guard, ct) =>
        {
            var reviewed = await links.ReviewAsync(SelectedLink!, ct); guard(); await LoadAsync(scope, guard);
            Status = reviewed.Review?.Kind == GoogleReviewKind.Unsafe ? "Google event is outside supported ownership/type rules. It was not changed; local detach remains available."
                : "Review saved. Choose a resolution explicitly; local and Google versions will be checked again.";
        }, () => SelectedLink?.State is GoogleLinkState.Linked or GoogleLinkState.Conflict, supported: true);
        UseGoogleCommand = Resolution(GoogleReviewChoice.UseGoogle, "Use reviewed Google version",
            "Replace the existing local block's title/times with the displayed Google version? If the local block is missing, only acknowledge the link; no block is recreated. Source task fields remain unchanged.");
        KeepLocalCommand = Resolution(GoogleReviewChoice.KeepLocal, "Keep local and push",
            "Push the displayed local block title/times over the reviewed Google version using a conditional write? A newer local or Google edit will reject this choice.");
        AcceptMissingCommand = Resolution(GoogleReviewChoice.AcceptMissing, "Accept Google deletion",
            "Close this link as deleted after checking absence again? The local block and task remain. A new publish requires a separate confirmation.");
        DetachCommand = Command(async (scope, guard, ct) =>
        {
            var selected = SelectedLink!;
            if (!await navigation.ConfirmGoogleEventAsync("Detach local Google link", $"Stop managing {selected.Desired.Title} in {selected.CalendarName} locally? No Google request will be made; its event may remain. New publishing may create another event. The local block/task remain.")) return;
            guard(); await links.DetachAsync(selected, ct); guard(); await LoadAsync(scope, guard);
            Status = "Detached locally. Google was not contacted; remove any old event separately if needed.";
        }, () => SelectedLink?.State is GoogleLinkState.Linked or GoogleLinkState.Conflict);
        CancelCommand = new AsyncCommand(() => { Cancel(); return Task.CompletedTask; }, () => busy, _ => { });
        session.Changed += Changed;
    }
    private AsyncCommand Resolution(GoogleReviewChoice choice, string action, string explanation) => Command(async (scope, guard, ct) =>
    {
        var selected = SelectedLink!;
        if (!await navigation.ConfirmGoogleEventAsync(action, selected.Review!.Details + "\n\n" + explanation)) return;
        guard(); var resolved = await links.ResolveAsync(selected, choice, ct); guard(); await LoadAsync(scope, guard);
        Status = resolved.State == GoogleLinkState.Conflict ? resolved.Problem : "Reviewed choice applied. Source task fields remain unchanged; no background synchronization runs.";
    }, () => SelectedLink?.Review is { } review && (choice == GoogleReviewChoice.AcceptMissing ? review.Kind == GoogleReviewKind.Missing
        : review.Kind == GoogleReviewKind.Present && (choice != GoogleReviewChoice.KeepLocal || review.Local is not null)), supported: true);
    private string Scope()
    {
        if (session.Current is not { } saved || !session.IsVerified || saved.ExpiresAt <= clock.GetUtcNow())
        { Clear(); throw new GoogleAuthorizationException("Sign in and verify the Todo account first. Only its account-scoped local blocks can be published."); }
        return SqliteSyncStore.Partition(saved.Origin, saved.User.Id);
    }
    private AsyncCommand Command(Func<string, Action, CancellationToken, Task> action, Func<bool>? enabled = null, bool supported = false) => new(async () =>
    {
        busy = true; Error = ""; operation = new(); Notify();
        try
        {
            var scope = Scope(); var version = session.Version;
            void Guard() { operation.Token.ThrowIfCancellationRequested(); if (session.Version != version || Scope() != scope) throw new GoogleAuthorizationException("The account changed; old Google event results were discarded."); }
            await action(scope, Guard, operation.Token); Guard();
        }
        catch (Exception exception) when (exception is GoogleAuthorizationException or GoogleCalendarHttpException or GoogleEventConflictException or CalendarConflictException or ArgumentException) { Error = exception.Message; }
        catch (OperationCanceledException) { Status = "Action canceled or timed out. A provider write may have finished; refresh and retry the saved operation to reconcile."; }
        catch (Exception) { Error = "Couldn't finish the Google event action. Refresh links and retry pending work; an uncertain write keeps its reserved identity and snapshot."; }
        finally { operation.Dispose(); operation = null; busy = false; Notify(); }
    }, () => !busy && (!supported || links.IsSupported) && (enabled?.Invoke() ?? true), _ => Error = "Couldn't finish the Google event action.");
    private async Task LoadAsync(string scope, Action guard)
    {
        var blocks = await store.ReadAsync(scope); guard(); var saved = await store.LinksAsync(scope); guard();
        var blockId = SelectedBlock?.Id; var linkId = SelectedLink?.BlockId;
        Blocks.Clear(); foreach (var row in blocks) Blocks.Add(row);
        Links.Clear(); foreach (var row in saved) Links.Add(row);
        SelectedBlock = Blocks.FirstOrDefault(b => b.Id == blockId); SelectedLink = Links.FirstOrDefault(l => l.BlockId == linkId);
        Status = $"{Blocks.Count} account-local blocks, {Links.Count} link records. Review differences before resolving them; no background synchronization runs.";
    }
    private void Changed() { Cancel(); Clear(); }
    private void Clear() { Blocks.Clear(); Links.Clear(); Calendars.Clear(); SelectedBlock = null; SelectedLink = null; SelectedCalendar = null; Status = "Todo account changed. Refresh the active account's blocks/links."; }
    public void Cancel() { try { operation?.Cancel(); } catch (ObjectDisposedException) { } }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy));
        foreach (var command in new[] { RefreshCommand, LoadCalendarsCommand, PublishCommand, DeleteCommand, RetryCommand, CancelCommand,
            ReviewCommand, UseGoogleCommand, KeepLocalCommand, AcceptMissingCommand, DetachCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() { session.Changed -= Changed; Cancel(); }
}
