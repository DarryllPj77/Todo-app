using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class GoogleConnectionViewModel : ObservableViewModel, IDisposable
{
    private readonly GoogleConnectionService google;
    private readonly ClientSession session;
    private readonly ITaskNavigation navigation;
    private CancellationTokenSource? operation;
    private bool busy;
    private int operationVersion;
    private string clientId = "", secret = "", error = "";
    private string status = "Sign in to a Todo account, then connect Google. Publish planned blocks separately through Google event links.";
    private GoogleConnectionInfo? connection;
    public string ClientId { get => clientId; set => SetProperty(ref clientId, value ?? ""); }
    public string ClientSecret { get => secret; set => SetProperty(ref secret, value ?? ""); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string ConnectionLabel => connection is { } info ? $"Google: {info.Email}\nToken expires: {info.ExpiresAt.ToLocalTime():g}" : "Google connection not checked or not connected.";
    public string PlatformNote => google.IsSupported ? "Windows desktop OAuth. Configure a Google Desktop app client first."
        : "Google authorization is disabled on this platform. Mobile authorization is a later increment; local blocks/export remain available.";
    public bool IsIdle => !busy;
    public bool IsBusy => busy;
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand ForgetCommand { get; }
    public AsyncCommand RevokeCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public GoogleConnectionViewModel(GoogleConnectionService google, ClientSession session, ITaskNavigation navigation)
    {
        this.google = google; this.session = session; this.navigation = navigation;
        ConnectCommand = Command(async ct => { SetConnection(await google.ConnectAsync(ClientId, ClientSecret, ct)); Status = "Google consent saved securely for this Todo account. Open Google event links for explicit publishing; no background sync runs."; }, supported: true);
        CheckCommand = Command(async ct => { SetConnection(await google.CheckAsync(ct)); Status = connection is null ? "No local Google connection. Enter a Desktop app client and Connect." : "Google identity verified; an expiring token was refreshed if needed. No calendar events were read or changed."; }, supported: true);
        ForgetCommand = Command(async ct =>
        {
            if (!await navigation.ConfirmGoogleDisconnectAsync(revoke: false)) return;
            ct.ThrowIfCancellationRequested(); await google.ForgetAsync(ct); SetConnection(null);
            Status = "Local Google credentials blocked/removed. Google permission remains until removed in your Google Account.";
        });
        RevokeCommand = Command(async ct =>
        {
            if (!await navigation.ConfirmGoogleDisconnectAsync(revoke: true)) return;
            ct.ThrowIfCancellationRequested(); await google.RevokeAsync(ct); SetConnection(null);
            Status = "Google confirmed revocation and local credentials were blocked/removed. Existing calendar events and blocks remain.";
        }, supported: true);
        CancelCommand = new AsyncCommand(() => { Cancel(); return Task.CompletedTask; }, () => busy, _ => { });
        session.Changed += AccountChanged;
    }
    private AsyncCommand Command(Func<CancellationToken, Task> action, bool supported = false) => new(async () =>
    {
        busy = true; operationVersion = session.Version; Error = ""; SetConnection(null); operation = new(); Notify();
        try { await action(operation.Token); }
        catch (Exception exception) when (exception is GoogleAuthorizationException or ApiClientException) { Error = exception.Message; }
        catch (OperationCanceledException) { Status = "Google action canceled or timed out. Check the connection before retrying."; }
        catch (Exception) { Error = "Couldn't finish the Google action. Check network, client setup and secure storage, then retry. Provider responses and tokens are not displayed."; }
        finally { ClientSecret = ""; operation.Dispose(); operation = null; busy = false; Notify(); }
    }, () => !busy && (!supported || google.IsSupported), _ => Error = "Couldn't finish the Google action.");
    private void SetConnection(GoogleConnectionInfo? value)
    {
        if (value is not null && operationVersion != session.Version)
            throw new GoogleAuthorizationException("The Todo account changed. Check the active account's connection again.");
        connection = value; OnPropertyChanged(nameof(ConnectionLabel));
    }
    private void AccountChanged() { Cancel(); SetConnection(null); ClientSecret = ""; Status = "Todo account/session changed. Check or connect Google for the active account."; }
    public void Cancel() { try { operation?.Cancel(); } catch (ObjectDisposedException) { } }
    private void Notify()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy));
        foreach (var command in new[] { ConnectCommand, CheckCommand, ForgetCommand, RevokeCommand, CancelCommand }) command?.NotifyCanExecuteChanged();
    }
    public void Dispose() { session.Changed -= AccountChanged; Cancel(); }
}
