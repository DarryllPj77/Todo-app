using System.Security.Cryptography;
using TodoApp.Domain.Location;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

// No cached-location reads, listener, background permission or startup request.
public sealed class ForegroundLocationProvider : IForegroundLocationProvider
{
    public async Task<PositionFix> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var position = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(20)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (position is null) throw new InvalidOperationException("No location fix was returned. Check location services and try again outdoors.");
            return new(position.Latitude, position.Longitude, position.Accuracy, position.Timestamp, position.IsFromMockProvider);
        }
        catch (PermissionException) { throw new InvalidOperationException("Location access was denied. Allow location while using Todo in device settings, then try again."); }
        catch (FeatureNotEnabledException) { throw new InvalidOperationException("Enable location services in device settings, then try again."); }
        catch (FeatureNotSupportedException) { throw new InvalidOperationException("This device does not support foreground location. Saved places can still be managed manually."); }
    }
}
public sealed class LocationEncryptionKeyStore : ILocationEncryptionKeyStore
{
    private const string Name = "todo.location.encryption.v1";
    public async Task<byte[]> GetAsync(bool allowCreate)
    {
        try
        {
            var encoded = await SecureStorage.Default.GetAsync(Name);
            if (encoded is not null)
            {
                var key = Convert.FromBase64String(encoded);
                if (key.Length == 32) return key;
                throw new InvalidDataException();
            }
            if (!allowCreate) throw new InvalidDataException();
            var created = RandomNumberGenerator.GetBytes(32);
            await SecureStorage.Default.SetAsync(Name, Convert.ToBase64String(created)); return created;
        }
        catch (Exception) { throw new InvalidDataException("The original protected location key could not be read/saved. Nothing was reset. Keep the original app data and OS credentials."); }
    }
}
// Debug workspace previews must never access real location or OS credentials.
public sealed class PreviewLocationProvider : IForegroundLocationProvider
{
    public Task<PositionFix> ReadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Location is unavailable in workspace preview.");
}
public sealed class PreviewLocationKeyStore : ILocationEncryptionKeyStore
{
    public Task<byte[]> GetAsync(bool allowCreate) => throw new InvalidOperationException("Saved places are unavailable in workspace preview. Use the normal app for protected storage.");
}
