using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class AndroidShareCaptureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoShare-" + Guid.NewGuid());
    private SqliteLocalTaskService Device => new(Path.Combine(directory, "device.db3"));
    private SqliteSyncStore Sync => new(Path.Combine(directory, "sync.db3"));
    private static CapturePrefill Parse(string? text, string? subject = null) =>
        AndroidShareCapture.Parse(AndroidShareCapture.SendAction, AndroidShareCapture.PlainText, text, subject);

    [Theory]
    [InlineData(null, "text/plain")]
    [InlineData("android.intent.action.SEND_MULTIPLE", "text/plain")]
    [InlineData("android.intent.action.VIEW", "text/plain")]
    [InlineData("android.intent.action.SEND", "text/html")]
    [InlineData("android.intent.action.SEND", "image/png")]
    [InlineData("android.intent.action.SEND", "text/*")]
    [InlineData("android.intent.action.SEND", null)]
    public void OnlySinglePlainTextSendIsAccepted(string? action, string? mime)
    {
        Assert.False(AndroidShareCapture.Accepts(action, mime));
        Assert.Equal(AndroidShareCapture.Rejected, AndroidShareCapture.Parse(action, mime, "private text"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    [InlineData("bad\0payload")]
    [InlineData("bad\u0001payload")]
    public void EmptyOrControlPayloadIsRejectedWithoutEchoingContent(string? text)
    {
        var input = Parse(text, "subject");
        Assert.Equal(AndroidShareCapture.Rejected, input);
        Assert.Empty(input.Title); Assert.Null(input.Description);
    }

    [Fact]
    public void SubjectBecomesTitleAndBodyIsPreservedAsPlainText()
    {
        var input = Parse("  <b>notes</b>\r\nhttps://example.invalid/private  ", "  Read later  ");
        Assert.Equal("Read later", input.Title);
        Assert.Equal("<b>notes</b>\nhttps://example.invalid/private", input.Description);
        Assert.Contains("Review", input.Notice);
    }

    [Fact]
    public void MissingSubjectUsesFirstNonblankLineAndRemainingDescription()
    {
        var input = Parse(" \r\n  First task  \r\n Notes\rMore notes \r\n ", "  ");
        Assert.Equal("First task", input.Title); Assert.Equal("Notes\nMore notes", input.Description);
        var single = Parse("  One line  ");
        Assert.Equal("One line", single.Title); Assert.Null(single.Description);
    }

    [Fact]
    public void LimitsAreCheckedWithoutTruncationOrPartialImport()
    {
        var title = new string('t', 200); var description = new string('d', 4000);
        var accepted = Parse(title + "\n" + description);
        Assert.Equal(title, accepted.Title); Assert.Equal(description, accepted.Description);
        Assert.Equal(AndroidShareCapture.Rejected, Parse(new string('t', 201)));
        Assert.Equal(AndroidShareCapture.Rejected, Parse("title\n" + new string('d', 4001)));
        Assert.Equal(AndroidShareCapture.Rejected, Parse(description + "d", "subject"));
        Assert.Equal(AndroidShareCapture.Rejected, Parse("body", new string('s', 201)));
        Assert.Equal(AndroidShareCapture.Rejected, Parse("body", "multi\nline subject"));
        Assert.Equal(AndroidShareCapture.Rejected, Parse(new string(' ', AndroidShareCapture.MaxIncomingTextLength) + "x"));
        Assert.Equal(AndroidShareCapture.Rejected, Parse("body", new string(' ', AndroidShareCapture.MaxIncomingSubjectLength) + "x"));
    }

    [Fact]
    public void UnsupportedSubjectControlsRejectTheWholePrefill()
    {
        Assert.Equal(AndroidShareCapture.Rejected, Parse("body", "secret\0subject"));
        Assert.Equal(AndroidShareCapture.Rejected, Parse("body", "secret\u007fsubject"));
    }

    [Fact]
    public async Task SharedDraftCanBeEditedAndSavedOnceWithoutTokenOrHttpAccess()
    {
        using var client = new ClientHarness(); client.Storage.FailRead = true;
        var input = Parse("https://example.invalid/private", "Read this");
        var vm = new QuickCaptureViewModel(new(Device, Sync, client.Session, client.Clock));
        Assert.Null(vm.Destination.Partition); Assert.Empty(await Device.GetTasksAsync());
        Assert.True(await vm.SaveAsync("Edited title", input.Description));
        Assert.True(await vm.SaveAsync("second title", "different text"));
        var task = Assert.Single(await Device.GetTasksAsync());
        Assert.Equal("Edited title", task.Title); Assert.Equal("https://example.invalid/private", task.Description);
        Assert.Equal(0, client.Storage.ReadCount); Assert.Empty(client.Handler.Requests);
    }

    [Fact]
    public async Task CancelSharedDraftDoesNotCreateTaskOrRestoreAccount()
    {
        using var client = new ClientHarness(); var input = Parse("shared task");
        var vm = new QuickCaptureViewModel(new(Device, Sync, client.Session, client.Clock));
        vm.Close(); Assert.False(await vm.SaveAsync(input.Title, input.Description));
        Assert.Empty(await Device.GetTasksAsync()); Assert.Equal(0, client.Storage.ReadCount);
        Assert.Empty(client.Handler.Requests);
    }

    [Fact]
    public async Task AccountDestinationStillRequiresExplicitSelectionAndQueuesOnlyLocally()
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var requests = client.Handler.Requests.Count;
        var vm = new QuickCaptureViewModel(new(Device, Sync, client.Session, client.Clock));
        var input = Parse("shared body", "shared title");
        Assert.Null(vm.Destination.Partition);
        await vm.SelectAccountAsync(); Assert.NotNull(vm.Destination.Partition);
        Assert.True(await vm.SaveAsync(input.Title, input.Description));
        var row = Assert.Single((await Sync.ReadAsync(vm.Destination.Partition!)).Rows).Value;
        Assert.Equal("shared title", row.Pending!.Task!.Title); Assert.Equal("shared body", row.Local!.Description);
        Assert.Empty(await Device.GetTasksAsync()); Assert.Equal(requests, client.Handler.Requests.Count);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
