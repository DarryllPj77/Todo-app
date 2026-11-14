using Android.App;
using Android.Content.PM;
using Android.OS;

namespace TodoApp.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var generation = Platforms.Android.AndroidBackgroundGeofenceHost.BeginLifecycleInvalidation();
        base.OnCreate(savedInstanceState);
        _ = Platforms.Android.AndroidBackgroundGeofenceHost.FinishLifecycleInvalidationAsync(generation);
    }
    protected override void OnResume()
    {
        base.OnResume();
        _ = Platforms.Android.AndroidBackgroundGeofenceHost.ReviewResumeAsync();
    }
}
