using System.ComponentModel.DataAnnotations;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class AccountViewModel : ObservableViewModel, IDisposable
{
    private readonly ClientSession session;
    private readonly TodoApiClient api;
    private bool isBusy;
    private bool initialized;
    private string email = "";
    private string password = "";
    private string endpoint;
    private string errorMessage = "";
    private string observedEndpoint;
    public ServerWorkspaceViewModel Workspace { get; }
    public bool IsBusy => isBusy;
    public bool IsIdle => !isBusy;
    public bool HasSession => session.Current is not null;
    public bool IsSignedOut => !HasSession;
    public bool HasError => ErrorMessage.Length > 0;
    public string Status => session.Current is { } saved
        ? $"{saved.User.Email} · {(session.IsVerified ? "verified" : "saved session; verify online")} · expires {saved.ExpiresAt.ToLocalTime():t}"
        : "Signed out. Device-local tasks remain available.";
    public string ActiveEndpoint => session.ConfiguredEndpoint;
    public string Email { get => email; set => SetProperty(ref email, value ?? ""); }
    public string Password { get => password; set => SetProperty(ref password, value ?? ""); }
    public string Endpoint { get => endpoint; set => SetProperty(ref endpoint, value ?? ""); }
    public string ErrorMessage
    {
        get => errorMessage;
        private set { SetProperty(ref errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }
    public AsyncCommand LoginCommand { get; }
    public AsyncCommand RegisterCommand { get; }
    public AsyncCommand LogoutCommand { get; }
    public AsyncCommand SaveEndpointCommand { get; }
    public AsyncCommand VerifyCommand { get; }

    public AccountViewModel(ClientSession session, TodoApiClient api, ServerWorkspaceViewModel workspace)
    {
        this.session = session;
        this.api = api;
        Workspace = workspace;
        endpoint = session.ConfiguredEndpoint;
        observedEndpoint = endpoint;
        LoginCommand = Command(() => AuthenticateAsync(register: false));
        RegisterCommand = Command(() => AuthenticateAsync(register: true));
        LogoutCommand = Command(session.SignOutAsync);
        SaveEndpointCommand = Command(async () => { await session.ChangeEndpointAsync(Endpoint); OnPropertyChanged(nameof(ActiveEndpoint)); });
        VerifyCommand = Command(() => api.VerifySessionAsync());
        session.Changed += SessionChanged;
    }

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        await RunAsync(async () =>
        {
            await session.RestoreAsync();
            if (session.Current is not null)
            {
                await api.VerifySessionAsync();
                await Workspace.RefreshCommand.ExecuteAsync();
            }
        });
    }

    private AsyncCommand Command(Func<Task> action) => new(() => RunAsync(action), () => !isBusy,
        _ => ErrorMessage = "Couldn't finish the account request. Please try again.");

    private async Task RunAsync(Func<Task> action)
    {
        isBusy = true; NotifyState(); ErrorMessage = "";
        try { await action(); }
        catch (ApiClientException exception) { ErrorMessage = exception.Message; }
        catch (Exception) { ErrorMessage = "Couldn't finish the account request. Device-local tasks are unaffected."; }
        finally { Password = ""; isBusy = false; NotifyState(); }
    }

    private async Task AuthenticateAsync(bool register)
    {
        if (string.IsNullOrWhiteSpace(Email) || !new EmailAddressAttribute().IsValid(Email.Trim()) ||
            Email.Trim().Length > 254 || Password.Length > 128 || Password.Length < (register ? 12 : 1))
        {
            ErrorMessage = register ? "Use a valid email and a password of 12–128 characters." : "Enter a valid email and your password.";
            return;
        }

        await api.SignInAsync(Email.Trim(), Password, register);
        await Workspace.RefreshCommand.ExecuteAsync();
    }

    private void SessionChanged()
    {
        Password = "";
        if (observedEndpoint != session.ConfiguredEndpoint)
        {
            observedEndpoint = session.ConfiguredEndpoint;
            Endpoint = observedEndpoint;
        }
        OnPropertyChanged(nameof(ActiveEndpoint));
        OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(IsSignedOut)); OnPropertyChanged(nameof(Status));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsIdle));
        LoginCommand.NotifyCanExecuteChanged(); RegisterCommand.NotifyCanExecuteChanged(); LogoutCommand.NotifyCanExecuteChanged();
        SaveEndpointCommand.NotifyCanExecuteChanged(); VerifyCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => session.Changed -= SessionChanged;
}
