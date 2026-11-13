namespace TodoApp.Maui.Services;

// Process-wide heartbeat continues when another desktop app has focus. Native
// Stopped/Destroying events interrupt only after all app windows stop/close.
public sealed class FocusRuntime(FocusService focus) : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<Window> visible = new();
    private CancellationTokenSource? cancellation;
    public async Task AttachAsync(Window window)
    {
        lock (gate)
        {
            visible.Add(window);
            if (cancellation is null)
            {
                cancellation = new(); var token = cancellation.Token;
                _ = Task.Run(async () =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                    try { while (await timer.WaitForNextTickAsync(token)) await focus.TickSafelyAsync(); }
                    catch (OperationCanceledException) { }
                }, token);
            }
        }
        await focus.ResumeAppSafelyAsync();
    }
    public async Task StoppedAsync(Window window)
    {
        bool stopped;
        lock (gate) { visible.Remove(window); stopped = visible.Count == 0; }
        if (stopped) await focus.SuspendAsync();
    }
    public Task ResumedAsync(Window window)
    { lock (gate) visible.Add(window); return focus.ResumeAppSafelyAsync(); }
    public Task DetachAsync(Window window) => StoppedAsync(window);
    public void Dispose() { lock (gate) { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; visible.Clear(); } }
}
