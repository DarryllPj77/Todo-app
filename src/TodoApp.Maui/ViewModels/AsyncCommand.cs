using System.Windows.Input;

namespace TodoApp.Maui.ViewModels;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool> canExecute;
    private readonly Action<Exception> onError;
    private bool isRunning;

    public AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError)
    {
        this.execute = execute;
        this.canExecute = canExecute;
        this.onError = onError;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !isRunning && canExecute();

    // ICommand is an event boundary; all exceptions are handled here.
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        isRunning = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
        finally
        {
            isRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
