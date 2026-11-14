using TodoApp.Domain.Recurrence;

namespace TodoApp.Domain.Location;

public enum ArrivalState { Armed, OutsideObserved, Attempted, Disabled, TaskChanged }
public enum Proximity { Uncertain, Outside, Inside }
public sealed record PositionFix(double Latitude, double Longitude, double? AccuracyMeters, DateTimeOffset CapturedUtc, bool Mocked = false);
public sealed record LocationTask(Guid Id, string Title, RepeatState Recurrence);
public sealed record LocationReminderDraft(Guid TaskId, string PlaceName, double Latitude, double Longitude, int RadiusMeters);
public sealed record LocationReminder(Guid Id, string Scope, long Revision, int NotificationId, Guid ArmId,
    Guid TaskId, string TaskTitle, RepeatState Recurrence, string PlaceName, double Latitude, double Longitude,
    int RadiusMeters, ArrivalState State, string Status)
{
    public bool Enabled => State is ArrivalState.Armed or ArrivalState.OutsideObserved;
    public string Label => $"{TaskTitle} · {PlaceName} · {State}";
    public override string ToString() => "Protected location reminder";
}
public static class LocationRules
{
    public static void Coordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 || !double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentException("Enter a latitude from -90 to 90 and longitude from -180 to 180.");
    }
    public static void Validate(LocationReminderDraft draft)
    {
        Coordinates(draft.Latitude, draft.Longitude);
        if (draft.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(draft.PlaceName) || draft.PlaceName.Trim().Length > 60 || draft.PlaceName.Any(char.IsControl))
            throw new ArgumentException("Choose a task and a place name of 1-60 characters without control characters.");
        if (draft.RadiusMeters is < 100 or > 5000) throw new ArgumentException("Choose a whole radius of 100-5000 meters.");
    }
    public static double DistanceMeters(double firstLatitude, double firstLongitude, double secondLatitude, double secondLongitude)
    {
        Coordinates(firstLatitude, firstLongitude); Coordinates(secondLatitude, secondLongitude);
        const double radians = Math.PI / 180;
        var latitude = (secondLatitude - firstLatitude) * radians; var longitude = (secondLongitude - firstLongitude) * radians;
        var value = Math.Pow(Math.Sin(latitude / 2), 2) + Math.Cos(firstLatitude * radians) * Math.Cos(secondLatitude * radians) * Math.Pow(Math.Sin(longitude / 2), 2);
        return 6371008.8 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(value, 0, 1)));
    }
    public static Proximity Classify(LocationReminder reminder, PositionFix fix, DateTimeOffset now)
    {
        if (fix.Mocked || fix.CapturedUtc > now.AddSeconds(10) || now - fix.CapturedUtc > TimeSpan.FromMinutes(2)
            || fix.AccuracyMeters is not { } accuracy || !double.IsFinite(accuracy) || accuracy <= 0 || accuracy > Math.Min(500, reminder.RadiusMeters / 2d)) return Proximity.Uncertain;
        try
        {
            var distance = DistanceMeters(reminder.Latitude, reminder.Longitude, fix.Latitude, fix.Longitude);
            if (distance + accuracy <= reminder.RadiusMeters) return Proximity.Inside;
            if (distance - accuracy >= reminder.RadiusMeters + 50) return Proximity.Outside;
            return Proximity.Uncertain;
        }
        catch (ArgumentException) { return Proximity.Uncertain; }
    }
}
