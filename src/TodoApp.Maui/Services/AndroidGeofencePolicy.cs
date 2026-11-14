using TodoApp.Domain.Location;

namespace TodoApp.Maui.Services;

public enum AndroidGeofenceAccess { Unavailable, NotificationsRequired, LocationServicesRequired, ForegroundPreciseRequired, BackgroundPromptRequired, BackgroundSettingsRequired, Ready }
public sealed record AndroidGeofenceAccessSnapshot(int ApiLevel, bool PlayServices, bool Notifications, bool LocationServices, bool Precise, bool Background);

public static class AndroidGeofencePolicy
{
    public static AndroidGeofenceAccess Next(AndroidGeofenceAccessSnapshot state)
    {
        if (state.ApiLevel < 23 || !state.PlayServices) return AndroidGeofenceAccess.Unavailable;
        if (!state.Notifications) return AndroidGeofenceAccess.NotificationsRequired;
        if (!state.LocationServices) return AndroidGeofenceAccess.LocationServicesRequired;
        if (!state.Precise) return AndroidGeofenceAccess.ForegroundPreciseRequired;
        if (state.ApiLevel >= 29 && !state.Background)
            return state.ApiLevel == 29 ? AndroidGeofenceAccess.BackgroundPromptRequired : AndroidGeofenceAccess.BackgroundSettingsRequired;
        return AndroidGeofenceAccess.Ready;
    }
    // Google geofencing sends dwell=4 and exit=2. Initial enter=1 is not an
    // arrival: the entry region requires dwell, and the core requires fresh outside.
    public static IReadOnlyList<BackgroundGeofenceReport> Reports(int transition, IReadOnlyList<string>? ids, PositionFix? fix)
    {
        if (ids is null || ids.Count is < 1 or > 2 || transition is not (2 or 4) || fix is null) return [];
        var suffix = transition == 4 ? "-enter" : "-exit";
        var kind = transition == 4 ? NativeGeofenceTransition.Entered : NativeGeofenceTransition.Exited;
        var result = new List<BackgroundGeofenceReport>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            const string prefix = "todo-background-v1-";
            if (id is null || !id.StartsWith(prefix, StringComparison.Ordinal) || !id.EndsWith(suffix, StringComparison.Ordinal)
                || id.Length != prefix.Length + 32 + suffix.Length || !Guid.TryParseExact(id.Substring(prefix.Length, 32), "N", out var run) || run == Guid.Empty) continue;
            result.Add(new(id, kind, fix));
        }
        return result;
    }
}
