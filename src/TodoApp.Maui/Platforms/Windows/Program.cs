namespace TodoApp.Maui.WinUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (args.Contains(Platforms.Windows.BackgroundGeofenceHost.Argument, StringComparer.Ordinal))
        { Platforms.Windows.BackgroundGeofenceHost.Run(); return; }
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
