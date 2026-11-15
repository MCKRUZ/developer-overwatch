namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

/// <summary>Read-only queries behind a session's context, cost-per-prompt and subagent views. Nothing it returns
/// is tracked, so a read can never be saved back by accident.</summary>
public interface ISessionInsightRepository
{
    Task<bool> SessionExistsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>The session's peak context and tool-error figures; null when the session does not exist.</summary>
    Task<SessionContextHeader?> GetContextHeaderAsync(string sessionId, CancellationToken ct = default);

    /// <summary>The newest main-thread turn that measured its context (newest by time, then by turn order);
    /// null when none has. Subagent turns run in their own context window, so they never count.</summary>
    Task<ContextReading?> GetLatestMainThreadContextAsync(string sessionId, CancellationToken ct = default);

    /// <summary>The session's latest context snapshot with all its rows; null when the Agent has sent none.</summary>
    Task<SessionContextSnapshot?> GetContextSnapshotAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Every turn of the session with its usage, ordered by time and then by turn order.</summary>
    Task<IReadOnlyList<SessionTurnCostRow>> GetTurnCostRowsAsync(string sessionId, CancellationToken ct = default);
}

public sealed record SessionContextHeader(
    long? PeakContextTokens,
    string? PeakContextModel,
    int ToolErrorCount,
    string? LastToolErrorName,
    DateTime? LastToolErrorAt);

public sealed record ContextReading(string? Model, long ContextTokens);

/// <summary>One turn's usage, with what is needed to group turns by the prompt that caused them.</summary>
/// <param name="PromptText">The start of the turn's content, at most <see cref="PromptTextLength"/> characters.
/// Set only on main-thread user turns, the only turns that can start a prompt; null on every other turn.</param>
public sealed record SessionTurnCostRow(
    int TurnIndex,
    DateTime Timestamp,
    string Role,
    string? TurnKind,
    string? SubagentName,
    string? PromptText,
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens,
    decimal? EstimatedCostUsd)
{
    /// <summary>Enough for an 80-character label after whitespace is collapsed, without loading whole prompts.</summary>
    public const int PromptTextLength = 400;

    public const string UserRole = "user";
    public const string SubagentRole = "subagent";
}
