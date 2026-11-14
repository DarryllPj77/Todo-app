using TodoApp.Domain.Entities;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed record CapturePrefill(string Title, string? Description, string Notice);

// Pure input policy; no URI, HTML, clipboard, credentials or storage access.
public static class AndroidShareCapture
{
    public const string SendAction = "android.intent.action.SEND";
    public const string PlainText = "text/plain";
    public const int MaxIncomingTextLength = 8192;
    public const int MaxIncomingSubjectLength = 512;
    public static CapturePrefill Rejected { get; } = new("", null,
        "Couldn't use this shared text. Share a shorter plain-text draft or enter a task yourself. Nothing was saved.");

    public static bool Accepts(string? action, string? mimeType) => action == SendAction && mimeType == PlainText;

    public static CapturePrefill Parse(string? action, string? mimeType, string? text, string? subject = null)
    {
        if (!Accepts(action, mimeType) || string.IsNullOrWhiteSpace(text) ||
            text.Length > MaxIncomingTextLength || subject?.Length > MaxIncomingSubjectLength ||
            HasUnsupportedControls(text) || (subject is not null && HasUnsupportedControls(subject))) return Rejected;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        subject = subject?.Trim();
        string title;
        string? description;
        if (!string.IsNullOrEmpty(subject))
        {
            title = subject;
            description = text;
        }
        else
        {
            var newline = text.IndexOf('\n');
            title = newline < 0 ? text : text[..newline];
            description = newline < 0 ? null : text[(newline + 1)..];
        }

        // Reject instead of silently losing text to the native field filters.
        if (title.Contains('\n') || title.Contains('\r') || title.Trim().Length > TaskItem.MaxTitleLength)
            return Rejected;
        try
        {
            var draft = new QuickCaptureDraft(title, description);
            return new(draft.Title, draft.Description, "Shared text is a draft. Review it before saving.");
        }
        catch (ArgumentException) { return Rejected; }
    }

    private static bool HasUnsupportedControls(string value) =>
        value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
}
