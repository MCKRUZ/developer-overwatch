namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;

public interface ISessionRepository
{
    /// <summary>
    /// Full-fidelity load, including ConversationTurns/ToolExecutions/FileChanges — used by
    /// internal consumers that need to scan/mutate that data (governance pattern evaluation,
    /// event ingestion). NOT for the dashboard's session-detail view — use
    /// <see cref="GetDetailAsync"/> for that (avoids loading potentially hundreds of rows just
    /// to show a header).
    /// </summary>
    Task<Session?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Lightweight session-detail projection: scalar fields + violations/annotations
    /// (small, cheap to eager-load) + turn/tool/file counts via COUNT, no row data.</summary>
    Task<SessionDetailProjection?> GetDetailAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<Session>> GetAllAsync(
        string? developerEmail = null,
        string? repo = null,
        DateTime? from = null,
        DateTime? to = null,
        SessionStatus? status = null,
        CancellationToken ct = default);

    /// <summary>Violation counts for a batch of sessions, keyed by session id — for list views
    /// that want a "has violations" badge without eager-loading each session's full collection.</summary>
    Task<IReadOnlyDictionary<string, int>> GetViolationCountsAsync(
        IEnumerable<string> sessionIds, CancellationToken ct = default);

    /// <summary>One page of this session's timeline (turns + tool executions + the file changes
    /// belonging to those tool executions), newest-first, strictly older than
    /// <paramref name="before"/> when given.</summary>
    Task<TimelinePage> GetTimelinePageAsync(
        string sessionId, DateTime? before, int limit, CancellationToken ct = default);

    /// <summary>One page of this session's timeline centered on a specific turn or tool
    /// execution — half the page is what led up to it, half is what came after. Used to jump
    /// straight to where a governance violation occurred instead of just opening the session.
    /// Returns null if neither id resolves to an event in this session.</summary>
    Task<TimelinePage?> GetTimelinePageAroundAsync(
        string sessionId, string? turnId, string? toolExecutionId, int limit, CancellationToken ct = default);

    /// <summary>Lightweight per-turn rows (timestamp/model/token/cost fields only, no content)
    /// for a session — the shared source data for both day-bucketing and model-mix breakdowns.</summary>
    Task<IReadOnlyList<TurnUsageRow>> GetTurnUsageRowsAsync(
        string sessionId, CancellationToken ct = default);

    /// <summary>Team-wide (all sessions, optionally scoped to one developer) turn usage rows
    /// within an optional date range, keyed by each turn's own timestamp — the source data for
    /// the team daily-cost trend chart.</summary>
    Task<IReadOnlyList<TurnUsageRow>> GetTurnUsageRowsForDateRangeAsync(
        DateTime? from, DateTime? to, string? developerEmail = null, CancellationToken ct = default);

    /// <summary>Per-turn usage rows (with SessionId) for a batch of sessions — the source data for
    /// per-session-per-day cost/token breakdowns on the sessions list (as opposed to each
    /// session's lifetime total).</summary>
    Task<IReadOnlyList<SessionTurnUsageRow>> GetTurnUsageRowsForSessionsAsync(
        IEnumerable<string> sessionIds, CancellationToken ct = default);

    /// <summary>Which of these conversation turn ids are already stored, in any session. Turn ids
    /// come from the transcript content itself, and a resumed session copies earlier history into
    /// its own transcript, so a resend can arrive under a different session than the original.</summary>
    Task<IReadOnlySet<string>> GetExistingTurnIdsAsync(
        IReadOnlyCollection<string> turnIds, CancellationToken ct = default);

    /// <summary>Which of these tool execution ids are already stored, in any session. A file change
    /// references its tool execution by foreign key, and the key does not depend on the session.</summary>
    Task<IReadOnlySet<string>> GetExistingToolExecutionIdsAsync(
        IReadOnlyCollection<string> toolExecutionIds, CancellationToken ct = default);

    /// <summary>Which of these tool execution ids are stored under this session. A file change may name
    /// only its own session's tool as its parent.</summary>
    Task<IReadOnlySet<string>> GetToolExecutionIdsInSessionAsync(
        string sessionId, IReadOnlyCollection<string> toolExecutionIds, CancellationToken ct = default);

    /// <summary>Which of these file change ids are already stored, in any session. The id is the
    /// primary key, so an id stored under another session is a duplicate, not a new row.</summary>
    Task<IReadOnlySet<string>> GetExistingFileChangeIdsAsync(
        IReadOnlyCollection<string> fileChangeIds, CancellationToken ct = default);

    /// <summary>Which of these tool failure ids are already stored, in any session. The id is the
    /// primary key, so the check is global.</summary>
    Task<IReadOnlySet<string>> GetExistingToolFailureIdsAsync(
        IReadOnlyCollection<string> failureIds, CancellationToken ct = default);

    Task AddToolFailuresAsync(IEnumerable<SessionToolFailure> failures, CancellationToken ct = default);

    /// <summary>Whether this session already processed a start or end with this Agent event id. Scoped
    /// to the session, so an id recorded for one session never makes another's event a duplicate.</summary>
    Task<bool> IsAgentEventProcessedAsync(string sessionId, string eventId, CancellationToken ct = default);

    /// <summary>Records a processed start or end; committed by the same unit of work as its effect.</summary>
    Task AddProcessedAgentEventAsync(ProcessedAgentEvent processedEvent, CancellationToken ct = default);

    Task AddAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default);
    Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default);
    Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default);
    Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default);
    Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default);
    Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default);
}

public sealed record SessionDetailProjection(
    Session Session,
    int ConversationTurnCount,
    int ToolExecutionCount,
    int FileChangeCount);

public sealed record TimelinePage(
    IReadOnlyList<ConversationTurn> ConversationTurns,
    IReadOnlyList<ToolExecution> ToolExecutions,
    IReadOnlyList<FileChange> FileChanges,
    DateTime? NextCursor,
    bool HasMore);

public sealed record TurnUsageRow(
    DateTime Timestamp,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens,
    decimal? EstimatedCostUsd);

public sealed record SessionTurnUsageRow(
    string SessionId,
    DateTime Timestamp,
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens,
    decimal? EstimatedCostUsd);
