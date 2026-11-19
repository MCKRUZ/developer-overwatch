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
    public string? GitBranch { get; set; }
    public string? SubagentName { get; set; }

    /// <summary>What started a user turn: one of <see cref="Common.TurnKinds"/>, or null for assistant and
    /// subagent turns and for turns from Agents that predate it.</summary>
    public string? TurnKind { get; set; }

    /// <summary>Context size of this request: input + cache read + cache creation tokens.
    /// Null when the turn reported none of them (e.g. a user turn).</summary>
    public long? ContextTokens { get; set; }

    public Session? Session { get; set; }
}
