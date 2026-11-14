using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Core.Location;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.Platforms.Android;

public sealed class AndroidBackgroundGeofencePlatform : IBackgroundGeofencePlatform, IBackgroundGeofencePermissionSettings
{
    internal const string Action = "todoapp.background-geofence.v1";
    private const int RequestCode = 0x47454F31;
    private static Context Context => global::Android.App.Application.Context;
    private static bool PlayServicesReady()
    {
        try { return GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Context) == ConnectionResult.Success; }
        catch { return false; } // An unavailable/corrupt native service cannot break manual task features.
    }
    public bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(23) && PlayServicesReady();
    public string Description => IsSupported
        ? "Android with Google Play services: one device reminder for up to 30 minutes. Precise location, background access and notifications are required. Cold Activity launch, reboot or app update invalidates saved runs; Start is always explicit."
        : "Android background geofences need Android 6+ and available Google Play services. Manual reminders remain usable; no background run is registered here.";
    public static async Task<AndroidGeofenceAccessSnapshot> AccessSnapshotAsync()
    {
        var location = (global::Android.Locations.LocationManager)Context.GetSystemService(Context.LocationService)!;
        var notification = await new AndroidReminderPlatform().PermissionAsync(false);
        return new((int)Build.VERSION.SdkInt, PlayServicesReady(),
            notification == ReminderPermission.Allowed, LocationManagerCompat.IsLocationEnabled(location),
            ContextCompat.CheckSelfPermission(Context, global::Android.Manifest.Permission.AccessFineLocation) == Permission.Granted,
            !OperatingSystem.IsAndroidVersionAtLeast(29) || ContextCompat.CheckSelfPermission(Context, global::Android.Manifest.Permission.AccessBackgroundLocation) == Permission.Granted);
    }
    public async Task RequestAccessAsync(CancellationToken token)
    {
        if (!IsSupported) throw new InvalidOperationException(Description);
        token.ThrowIfCancellationRequested(); var state = await AccessSnapshotAsync();
        if (AndroidGeofencePolicy.Next(state) == AndroidGeofenceAccess.ForegroundPreciseRequired)
        {
            await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.LocationWhenInUse>());
            token.ThrowIfCancellationRequested(); state = await AccessSnapshotAsync();
        }
        if (AndroidGeofencePolicy.Next(state) == AndroidGeofenceAccess.BackgroundPromptRequired)
        {
            // Android 10 only: background access is requested separately after
            // fine foreground access. Android 11+ uses the explicit settings button.
            await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<BackgroundPermission>());
            token.ThrowIfCancellationRequested(); state = await AccessSnapshotAsync();
        }
        var next = AndroidGeofencePolicy.Next(state);
        if (next != AndroidGeofenceAccess.Ready) throw new InvalidOperationException(next switch
        {
            AndroidGeofenceAccess.NotificationsRequired => "Use Allow/check notifications before starting an Android background run.",
            AndroidGeofenceAccess.LocationServicesRequired => "Enable Android location services, then explicitly retry Start.",
            AndroidGeofenceAccess.ForegroundPreciseRequired => "Allow precise location while using Todo. Approximate access cannot enable geofencing; manual checks remain available.",
            AndroidGeofenceAccess.BackgroundPromptRequired or AndroidGeofenceAccess.BackgroundSettingsRequired => "Use Open Android location permission settings, enable Allow all the time and precise location, then return and press Start again. You can decline and keep manual reminders.",
            _ => Description
        });
        token.ThrowIfCancellationRequested();
    }
    private sealed class BackgroundPermission : Permissions.BasePlatformPermission
    { public override (string androidPermission, bool isRuntime)[] RequiredPermissions => OperatingSystem.IsAndroidVersionAtLeast(29) ? [(global::Android.Manifest.Permission.AccessBackgroundLocation, true)] : []; }
    public Task OpenPermissionSettingsAsync(CancellationToken token) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        var intent = new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings, global::Android.Net.Uri.Parse("package:" + Context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask); Context.StartActivity(intent);
    });
    private static Intent ReceiverIntent() => new Intent(Context, typeof(BackgroundGeofenceReceiver)).SetAction(Action)!;
    private static PendingIntentFlags Flags => PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
    private static PendingIntent Pending() => PendingIntent.GetBroadcast(Context, RequestCode, ReceiverIntent(), Flags)!;
    public async Task RegisterAsync(BackgroundGeofenceRun run, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (AndroidGeofencePolicy.Next(await AccessSnapshotAsync()) != AndroidGeofenceAccess.Ready)
            throw new InvalidOperationException("Android permissions/services changed before registration. Review settings and explicitly retry.");
        var row = run.Reminder; var shape = new NativeGeofenceSpec(row.Id, row.ArmId, row.Latitude, row.Longitude, row.RadiusMeters);
        var duration = (long)(run.ExpiresUtc - DateTimeOffset.UtcNow).TotalMilliseconds;
        if (duration <= 0) throw new InvalidOperationException("Android background registration expired before completion.");
        var entry = new GeofenceBuilder().SetRequestId(run.EntryId)!.SetCircularRegion(row.Latitude, row.Longitude, (float)shape.EntryRadius)!
            .SetExpirationDuration(duration)!.SetTransitionTypes(4)!.SetLoiteringDelay(10_000)!.SetNotificationResponsiveness(60_000)!.Build()!;
        var exit = new GeofenceBuilder().SetRequestId(run.ExitId)!.SetCircularRegion(row.Latitude, row.Longitude, (float)shape.ExitRadius)!
            .SetExpirationDuration(duration)!.SetTransitionTypes(2)!.SetNotificationResponsiveness(60_000)!.Build()!;
        var request = new GeofencingRequest.Builder().SetInitialTrigger(0)!.AddGeofences(new List<IGeofence> { entry, exit })!.Build()!;
        using var client = LocationServices.GetGeofencingClient(Context);
        // Keep the registration lease until Play services acknowledges Add.
        // Cancelling only the managed wait could let a late Add race cleanup.
        await client.AddGeofencesAsync(request, Pending());
        token.ThrowIfCancellationRequested();
    }
    public async Task CleanupAsync()
    {
        var flags = PendingIntentFlags.NoCreate | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
        var pending = PendingIntent.GetBroadcast(Context, RequestCode, ReceiverIntent(), flags);
        if (pending is null) return;
        // Removing by this feature's exact explicit PendingIntent cannot remove
        // time-reminder alarms or another feature's geofences.
        using var client = LocationServices.GetGeofencingClient(Context);
        await client.RemoveGeofencesAsync(pending).WaitAsync(TimeSpan.FromSeconds(4));
        pending.Cancel(); pending.Dispose();
    }
}
