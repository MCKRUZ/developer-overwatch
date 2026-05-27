namespace DeveloperOverwatch.Agent.Models;

public sealed class QueuedEvent
{
    public long RowId { get; init; }
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required EventType EventType { get; init; }
    public required string PayloadJson { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int RetryCount { get; init; }
    public bool Shipped { get; init; }
}
