using System.Globalization;

namespace TodoApp.Maui;

public sealed class WorkspaceNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() switch
    { "Device" => "On this device", "SyncedAccount" => "My account", _ => value?.ToString() ?? "" };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
