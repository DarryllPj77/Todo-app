using global::Android.App;
using global::Android.Content.PM;
using global::Android.OS;
using global::Android.Text;
using global::Android.Views;
using global::Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Maui.ViewModels;
using Button = global::Android.Widget.Button;
using View = global::Android.Views.View;
using ScrollView = global::Android.Widget.ScrollView;
using Orientation = global::Android.Widget.Orientation;

namespace TodoApp.Maui.Platforms.Android;

// A separate task keeps launcher shortcut flags away from the main editor.
[Activity(Name = "com.companyname.todoapp.maui.QuickCaptureActivity", Exported = true,
    Theme = "@android:style/Theme.Material.Light.Dialog.NoActionBar", Label = "Quick capture",
    TaskAffinity = "", ExcludeFromRecents = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class QuickCaptureActivity : Activity
{
    public const string CaptureAction = "com.companyname.todoapp.maui.QUICK_CAPTURE";
    private QuickCaptureViewModel? model;
    private EditText title = null!;
    private EditText description = null!;
    private TextView destination = null!;
    private TextView error = null!;
    private Button device = null!;
    private Button account = null!;
    private Button save = null!;
    private Button cancel = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Exported activation never imports text, account IDs, tokens or URLs.
        if (Intent?.Action != CaptureAction) { Finish(); return; }
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        layout.SetPadding(padding, padding, padding, padding);
        var heading = new TextView(this) { Text = "Quick capture", TextSize = 24 };
        layout.AddView(heading);
        title = new EditText(this) { Hint = "Task title (required)", InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences, SaveEnabled = false };
        title.SetSingleLine(true);
        title.SetFilters(new IInputFilter[] { new InputFilterLengthFilter(200) });
        description = new EditText(this) { Hint = "Description (optional)", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, SaveEnabled = false };
        description.SetMaxLines(4);
        description.SetFilters(new IInputFilter[] { new InputFilterLengthFilter(4000) });
        layout.AddView(title);
        layout.AddView(description);
        destination = new TextView(this);
        error = new TextView(this) { AccessibilityLiveRegion = AccessibilityLiveRegion.Polite };
        device = new Button(this) { Text = "Device only" };
        account = new Button(this) { Text = "Choose current account" };
        save = new Button(this) { Text = "Save task" };
        cancel = new Button(this) { Text = "Cancel" };
        foreach (var view in new View[] { destination, device, account, error, save, cancel }) layout.AddView(view);
        var scroll = new ScrollView(this);
        scroll.AddView(layout);
        SetContentView(scroll);
        try
        {
            model = IPlatformApplication.Current!.Services.GetRequiredService<QuickCaptureViewModel>();
        }
        catch (Exception)
        {
            error.Text = "Couldn't open capture. Close and open the main app before trying again.";
            device.Enabled = account.Enabled = save.Enabled = false;
        }
        device.Click += (_, _) => { model?.SelectDevice(); Render(); };
        account.Click += async (_, _) =>
        {
            if (model is null) return;
            var selection = model.SelectAccountAsync(); Render();
            await selection;
            if (!IsDestroyed) Render();
        };
        save.Click += async (_, _) =>
        {
            if (model is null) return;
            var saving = model.SaveAsync(title.Text ?? "", description.Text); Render();
            var saved = await saving;
            if (IsDestroyed) return;
            if (saved)
            {
                Toast.MakeText(this, model.Destination.Partition is null ? "Task saved on this device" : "Task queued for account sync", ToastLength.Short)?.Show();
                Finish();
            }
            else Render();
        };
        cancel.Click += (_, _) => Finish();
        Render();
    }

    private void Render()
    {
        if (model is null) return;
        destination.Text = "Save to: " + model.Destination.Label;
        error.Text = model.Error;
        title.Enabled = description.Enabled = device.Enabled = account.Enabled = model.CanEdit;
        save.Enabled = cancel.Enabled = !model.IsBusy && !model.IsSaved;
        save.Text = model.IsBusy ? "Working…" : model.CanEdit ? "Save task" : "Retry same save";
    }

    protected override void OnDestroy()
    {
        model?.Close();
        base.OnDestroy();
    }
}
