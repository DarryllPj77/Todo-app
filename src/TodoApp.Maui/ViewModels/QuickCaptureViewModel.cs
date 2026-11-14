using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class QuickCaptureViewModel(QuickCaptureService captures)
{
    private readonly Guid captureId = Guid.NewGuid();
    private QuickCaptureDraft? submitted;
    private bool closed;
    public CaptureDestination Destination { get; private set; } = CaptureDestination.Device;
    public bool IsBusy { get; private set; }
    public bool IsSaved { get; private set; }
    public bool CanEdit => !closed && !IsBusy && submitted is null && !IsSaved;
    public string Error { get; private set; } = "";

    public void SelectDevice()
    {
        if (CanEdit) { Destination = CaptureDestination.Device; Error = ""; }
    }

    public async Task SelectAccountAsync()
    {
        if (!CanEdit) return;
        IsBusy = true;
        try
        {
            var selected = await captures.SelectAccountAsync();
            if (!closed) { Destination = selected; Error = ""; }
        }
        catch (Exception) { Error = "Open the app and sign in before choosing an account. Device capture is available offline."; }
        finally { IsBusy = false; }
    }

    public async Task<bool> SaveAsync(string title, string? description)
    {
        if (closed || IsBusy || IsSaved) return IsSaved;
        IsBusy = true;
        try
        {
            // Freeze the normalized payload and destination across all retries.
            // Invalid input remains editable; uncertain storage failures do not
            // start a second capture or change the intended workspace.
            submitted ??= new QuickCaptureDraft(title, description);
            await captures.SaveAsync(captureId, submitted, Destination);
            IsSaved = true;
            Error = "";
            return true;
        }
        catch (ArgumentException ex) { Error = ex.Message; return false; }
        catch (Exception)
        {
            Error = "Couldn't confirm this save. Retry the same draft, or close and check the selected workspace before starting another capture.";
            return false;
        }
        finally { IsBusy = false; }
    }

    public void Close() => closed = true;
}
