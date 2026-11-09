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

    public IList<ConversationTurn> ConversationTurns { get; set; } = new List<ConversationTurn>();
    public IList<ToolExecution> ToolExecutions { get; set; } = new List<ToolExecution>();
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
    public IList<GovernanceViolation> GovernanceViolations { get; set; } = new List<GovernanceViolation>();
    public IList<CommitSessionLink> CommitSessionLinks { get; set; } = new List<CommitSessionLink>();
    public IList<ArchitectAnnotation> ArchitectAnnotations { get; set; } = new List<ArchitectAnnotation>();
}
