namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

/// <summary>A session start or end the Agent sent, recorded by its event id so a redelivery is
/// recognised and changes nothing. Kept forever: the rows are tiny and must outlive any queue the
/// Agent could still redeliver from. Not tied to the session by a foreign key, so deleting a session
/// does not make its old events look new.</summary>
public class ProcessedAgentEvent
{
    public string EventId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ProcessedAgentEventKind Kind { get; set; }
    public DateTime ProcessedAt { get; set; }
}
