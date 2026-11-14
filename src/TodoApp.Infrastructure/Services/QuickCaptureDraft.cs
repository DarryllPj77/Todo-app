using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TodoApp.Domain.Entities;

namespace TodoApp.Infrastructure.Services;

// Only the ordinary task fields are accepted at this boundary.
public sealed record QuickCaptureDraft
{
    public string Title { get; }
    public string? Description { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { Title, Description }))));

    public QuickCaptureDraft(string title, string? description = null)
    {
        var task = new TaskItem(title, description);
        Title = task.Title;
        Description = task.Description;
    }
}

public sealed record QuickCaptureResult(Guid TaskId, bool AlreadyCaptured);
