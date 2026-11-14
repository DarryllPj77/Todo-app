namespace TodoApp.Maui.Services;

public sealed class BackgroundGeofenceBlocker(string path) : IBackgroundGeofenceBlocker
{
    private readonly TodoApp.Infrastructure.Services.SqliteBackgroundGeofenceControl control = new(path);
    public bool IsBlocked => control.Read().Blocked;
    public long Generation => control.Read().Generation;
    public long Block() => control.Block();
    public void Enable(long generation) => control.Enable(generation);
}
public sealed class PreviewBackgroundGeofenceBlocker : IBackgroundGeofenceBlocker
{
    public bool IsBlocked { get; private set; } = true;
    public long Generation { get; private set; }
    public long Block() { IsBlocked = true; return ++Generation; }
    public void Enable(long generation) { if (generation != Generation) throw new OperationCanceledException(); IsBlocked = false; }
}
