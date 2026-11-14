using System.Runtime.InteropServices;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using global::Windows.ApplicationModel.Background;
using global::Windows.UI.Notifications;
using WinRT;

namespace TodoApp.Maui.Platforms.Windows;

public static class BackgroundGeofenceHost
{
    public const string Argument = "--todo-geofence-background";
    private static readonly ManualResetEventSlim completed = new();
    public static void Run()
    {
        if (WorkspacePreview.Enabled) return;
        try { _ = global::Windows.ApplicationModel.Package.Current.Id.Name; } catch { return; }
        // COM callbacks execute on an MTA. No MAUI application, window, HTTP
        // client, account restoration or foreground runtime is constructed.
        var thread = new Thread(() =>
        {
            var id = WindowsBackgroundGeofencePlatform.TaskClassId;
            var factory = new Factory(); var pointer = Marshal.GetIUnknownForObject(factory);
            uint registration = 0;
            try
            {
                // REGCLS_SINGLEUSE: each activation gets its own bounded host;
                // completing one task must not terminate a second task in flight.
                Marshal.ThrowExceptionForHR(CoRegisterClassObject(ref id, pointer, 4, 0, out registration));
                completed.Wait(TimeSpan.FromSeconds(60));
            }
            finally { if (registration != 0) CoRevokeClassObject(registration); Marshal.Release(pointer); GC.KeepAlive(factory); }
        });
        thread.SetApartmentState(ApartmentState.MTA); thread.Start(); thread.Join();
    }
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRegisterClassObject(ref Guid id, IntPtr factory, uint context, uint flags, out uint registration);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoRevokeClassObject(uint registration);
    [ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr outer, ref Guid requested, out IntPtr result);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Factory : IClassFactory
    {
        private int created;
        public int CreateInstance(IntPtr outer, ref Guid requested, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
            if (Interlocked.Exchange(ref created, 1) != 0) return unchecked((int)0x80040111);
            var pointer = MarshalInterface<IBackgroundTask>.FromManaged(new TaskHandler());
            try { return Marshal.QueryInterface(pointer, in requested, out result); }
            finally { Marshal.Release(pointer); }
        }
        public int LockServer(bool locked) => 0;
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None), Guid("93208D98-A7E6-4E58-AAF3-CDB720D94E77")]
    public sealed class TaskHandler : IBackgroundTask
    {
        private int used;
        public async void Run(IBackgroundTaskInstance instance)
        {
            var deferral = instance.GetDeferral();
            if (Interlocked.Exchange(ref used, 1) != 0) { deferral.Complete(); return; }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            BackgroundTaskCanceledEventHandler onCanceled = (_, _) => { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }; instance.Canceled += onCanceled;
            try
            {
                if (instance.Task.Name != WindowsBackgroundGeofencePlatform.TaskName) return;
                var directory = FileSystem.AppDataDirectory;
                var blocker = new BackgroundGeofenceBlocker(Path.Combine(directory, "geofence-control.db3"));
                var platform = new WindowsBackgroundGeofencePlatform(Path.Combine(directory, "geofence-background.db3"));
                var service = DeviceBackgroundGeofenceHost.Create(directory, new LocationEncryptionKeyStore(), platform, new HeadlessNotifications(), blocker);
                await service.ProcessAsync(WindowsBackgroundGeofencePlatform.ReadReports, cancellation.Token);
            }
            catch { /* No sensitive payload/logging and no UI fallback on activation failure. */ }
            finally { instance.Canceled -= onCanceled; deferral.Complete(); completed.Set(); }
        }
    }
    private sealed class HeadlessNotifications : IReminderPlatform
    {
        private static string AppId => global::Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
        public bool SupportsScheduling => false;
        public string DeliveryDescription => "Generic packaged background arrival attempt.";
        public Task<ReminderPermission> PermissionAsync(bool request) => Task.FromResult(ToastNotificationManager.CreateToastNotifier(AppId).Setting == NotificationSetting.Enabled ? ReminderPermission.Allowed : ReminderPermission.Denied);
        public Task ScheduleAsync(int id, DateTimeOffset when) => throw new NotSupportedException();
        public Task ShowAsync(int id)
        {
            var xml = new global::Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>Todo reminder</text><text>A task reminder is due. Open Todo to review it.</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml) { Tag = id.ToString(System.Globalization.CultureInfo.InvariantCulture), Group = "todo-reminders" });
            return Task.CompletedTask;
        }
        public Task CancelAsync(int id) { ToastNotificationManager.History.Remove(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "todo-reminders", AppId); return Task.CompletedTask; }
    }
}
