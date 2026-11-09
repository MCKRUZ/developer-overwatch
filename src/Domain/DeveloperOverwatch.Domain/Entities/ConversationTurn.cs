namespace DeveloperOverwatch.Domain.Entities;

public class ConversationTurn
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int TurnIndex { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? TokenCount { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public string? Model { get; set; }
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>Date of the price table that computed <see cref="EstimatedCostUsd"/>.</summary>
    public DateOnly? PricesAsOf { get; set; }
    public bool ContainsRedactedSecret { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsDecision { get; set; }

    public Session? Session { get; set; }
}
