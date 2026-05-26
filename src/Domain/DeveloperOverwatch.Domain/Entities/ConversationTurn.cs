namespace DeveloperOverwatch.Domain.Entities;

public class ConversationTurn
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int TurnIndex { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? TokenCount { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsDecision { get; set; }

    public Session? Session { get; set; }
}
