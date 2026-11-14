using global::Android.App;
using global::Android.Appwidget;
using global::Android.Content;
using global::Android.Widget;

namespace TodoApp.Maui.Platforms.Android;

// Stateless entry only: a launcher never receives tasks, accounts or drafts.
[BroadcastReceiver(Name = "com.companyname.todoapp.maui.QuickCaptureWidgetProvider",
    Enabled = true, Exported = false, Label = "@string/quick_capture_widget_name")]
[IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
[MetaData("android.appwidget.provider", Resource = "@xml/quick_capture_widget_info")]
public sealed class QuickCaptureWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;
        // A direct Activity PendingIntent, with no user data or workspace identity.
        // MultipleTask preserves any existing main editor or capture draft.
        using var intent = new Intent(context, typeof(QuickCaptureActivity));
        intent.SetAction(QuickCaptureActivity.CaptureAction);
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.MultipleTask);
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        var openCapture = PendingIntent.GetActivity(context, 1503, intent, flags);
        if (openCapture is null) return;

        // Every instance is identical; no timer, DI graph, database or token read.
        foreach (var id in appWidgetIds)
        {
            using var views = new RemoteViews(context.PackageName, Resource.Layout.quick_capture_widget);
            views.SetOnClickPendingIntent(Resource.Id.quick_capture_widget_button, openCapture);
            appWidgetManager.UpdateAppWidget(id, views);
        }
    }
}
