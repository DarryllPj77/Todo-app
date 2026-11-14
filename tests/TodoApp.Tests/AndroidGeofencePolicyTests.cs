using TodoApp.Domain.Location;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed class AndroidGeofencePolicyTests
{
    [Theory]
    [InlineData(21, true, true, true, true, true, AndroidGeofenceAccess.Unavailable)]
    [InlineData(36, false, true, true, true, true, AndroidGeofenceAccess.Unavailable)]
    [InlineData(36, true, false, true, true, true, AndroidGeofenceAccess.NotificationsRequired)]
    [InlineData(36, true, true, false, true, true, AndroidGeofenceAccess.LocationServicesRequired)]
    [InlineData(36, true, true, true, false, true, AndroidGeofenceAccess.ForegroundPreciseRequired)]
    [InlineData(29, true, true, true, false, false, AndroidGeofenceAccess.ForegroundPreciseRequired)]
    [InlineData(29, true, true, true, true, false, AndroidGeofenceAccess.BackgroundPromptRequired)]
    [InlineData(30, true, true, true, true, false, AndroidGeofenceAccess.BackgroundSettingsRequired)]
    [InlineData(36, true, true, true, true, false, AndroidGeofenceAccess.BackgroundSettingsRequired)]
    [InlineData(28, true, true, true, true, false, AndroidGeofenceAccess.Ready)]
    [InlineData(29, true, true, true, true, true, AndroidGeofenceAccess.Ready)]
    [InlineData(36, true, true, true, true, true, AndroidGeofenceAccess.Ready)]
    public void PermissionProgression_IsStagedAndVersionSpecific(int api, bool play, bool notifications, bool services, bool precise, bool background, AndroidGeofenceAccess expected) =>
        Assert.Equal(expected, AndroidGeofencePolicy.Next(new(api, play, notifications, services, precise, background)));

    private static PositionFix Fix => new(0, 0, 10, new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
    private static string Entry => "todo-background-v1-" + Guid.NewGuid().ToString("N") + "-enter";
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)] [InlineData(-1)]
    public void InitialEnterOrUnknownTransition_IsNotArrival(int transition) => Assert.Empty(AndroidGeofencePolicy.Reports(transition, [Entry], Fix));
    [Fact]
    public void DwellAndExit_MapToExactOpaqueIdsOnly()
    {
        var entry = Entry; var exit = entry.Replace("-enter", "-exit");
        var arrived = Assert.Single(AndroidGeofencePolicy.Reports(4, [entry, exit], Fix));
        Assert.Equal(entry, arrived.NativeId); Assert.Equal(NativeGeofenceTransition.Entered, arrived.Transition);
        var left = Assert.Single(AndroidGeofencePolicy.Reports(2, [entry, exit], Fix));
        Assert.Equal(exit, left.NativeId); Assert.Equal(NativeGeofenceTransition.Exited, left.Transition);
    }
    [Theory]
    [InlineData("")] [InlineData("foreign-enter")] [InlineData("todo-background-v1-bad-enter")]
    [InlineData("todo-background-v1-00000000000000000000000000000000-enter")]
    [InlineData("todo-background-v1-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz-enter")]
    public void InvalidOrForeignIdentity_IsIgnored(string id) => Assert.Empty(AndroidGeofencePolicy.Reports(4, [id], Fix));
    [Fact]
    public void MissingFixOrOversizedPayload_CannotCreateReports()
    {
        Assert.Empty(AndroidGeofencePolicy.Reports(4, [Entry], null));
        Assert.Empty(AndroidGeofencePolicy.Reports(4, null, Fix));
        Assert.Empty(AndroidGeofencePolicy.Reports(4, [Entry, Entry, Entry], Fix));
    }
    [Fact]
    public void DuplicateNativeIds_AreProcessedOnce()
    { var id = Entry; Assert.Single(AndroidGeofencePolicy.Reports(4, [id, id], Fix)); }
}
