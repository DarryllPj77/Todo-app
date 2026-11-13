namespace TodoApp.Infrastructure.Persistence.Server;

// Latest state, including deletions, retained until a future reviewed retention policy.
public sealed class SyncRecord
{
    public Guid OwnerId { get; set; }
    public Guid TaskId { get; set; }
    public long Revision { get; set; }
    public bool Deleted { get; set; }
    public string? Payload { get; set; }
}

public sealed class SyncReceipt
{
    public Guid OwnerId { get; set; }
    public Guid OperationId { get; set; }
    public string Request { get; set; } = "";
    public string Response { get; set; } = "";
}
