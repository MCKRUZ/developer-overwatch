namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class Session
{
    public string Id { get; set; } = string.Empty;
    public string DeveloperEmail { get; set; } = string.Empty;
    public string? DeveloperName { get; set; }
    public string? RepoPath { get; set; }
    public string? Title { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? SummaryJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Bumped on SessionStart/resume and on every successful event batch — the source of truth
    // for whether an "Active" session is genuinely live or just never got a SessionEnd (crash,
    // laptop sleep, a dropped request). Never used to close a session automatically: a session
    // resumed after any amount of idle time reactivates exactly as before, since Status itself
    // is untouched — staleness is a read-time display computation, not a stored state.
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    // Running totals across all conversation turns, maintained incrementally as turns are
    // ingested — lets the dashboard show session-level usage/cost without summing turns client-side.
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheCreationInputTokens { get; set; }
    public long TotalCacheReadInputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    public const string DefaultAgent = "claude-code";

    // Agents that predate this field never sent one, and every one of them was Claude Code — so
    // that is the default here and the backfill for existing rows, and every row carries an agent.
    public string Agent { get; set; } = DefaultAgent;
    public string? MachineName { get; set; }

    // A stable identity for the repository (e.g. a normalized remote), so the same project groups
    // together across machines and clone paths. The Agent strips credentials before sending.
    public string? ProjectKey { get; set; }

    // Largest single-request context (input + cache read + cache creation) seen in this session.
    // Only ever raised as turns arrive.
    public long? PeakContextTokens { get; set; }

    public ActivityState? ActivityState { get; set; }
    public DateTime? ActivityStateAt { get; set; }

    // Tool failures carry no ids, so a redelivered batch would count twice; the Agent ships each
    // failure exactly once (cursor-tracked) and the server just adds what it receives.
    public int ToolErrorCount { get; set; }
    public string? LastToolErrorName { get; set; }
    public DateTime? LastToolErrorAt { get; set; }

    public IList<ConversationTurn> ConversationTurns { get; set; } = new List<ConversationTurn>();
    public IList<ToolExecution> ToolExecutions { get; set; } = new List<ToolExecution>();
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
    public IList<GovernanceViolation> GovernanceViolations { get; set; } = new List<GovernanceViolation>();
    public IList<CommitSessionLink> CommitSessionLinks { get; set; } = new List<CommitSessionLink>();
    public IList<ArchitectAnnotation> ArchitectAnnotations { get; set; } = new List<ArchitectAnnotation>();
}
