namespace TodoApp.Maui.Views;

internal static class WorkspaceUi
{
    public static Label Heading(string text) => new() { Text = text, FontSize = 24, FontAttributes = FontAttributes.Bold };
    public static Label Note(string text) => new() { Text = text, FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };
    public static Button Action(string text, Func<Task> action)
    {
        var button = new Button { Text = text, HorizontalOptions = LayoutOptions.Fill, MinimumHeightRequest = 44 };
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception) { if (Application.Current?.Windows.FirstOrDefault()?.Page is { } page) await page.DisplayAlertAsync("Couldn't open this view", "Your saved work is unchanged. Return and try again.", "OK"); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
    public static VerticalStackLayout Stack(params View[] views)
    {
        var stack = new VerticalStackLayout { Padding = 20, Spacing = 12, MaximumWidthRequest = 1000, HorizontalOptions = LayoutOptions.Fill };
        foreach (var view in views) stack.Add(view);
        return stack;
    }
    public static Label Bound(string path)
    {
        var label = Note(""); label.SetBinding(Label.TextProperty, path); return label;
    }
}
