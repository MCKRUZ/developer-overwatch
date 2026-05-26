namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class Session
{
    public string Id { get; set; } = string.Empty;
    public string DeveloperEmail { get; set; } = string.Empty;
    public string? DeveloperName { get; set; }
    public string? RepoPath { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? SummaryJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public IList<ConversationTurn> ConversationTurns { get; set; } = new List<ConversationTurn>();
    public IList<ToolExecution> ToolExecutions { get; set; } = new List<ToolExecution>();
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
    public IList<GovernanceViolation> GovernanceViolations { get; set; } = new List<GovernanceViolation>();
    public IList<CommitSessionLink> CommitSessionLinks { get; set; } = new List<CommitSessionLink>();
    public IList<ArchitectAnnotation> ArchitectAnnotations { get; set; } = new List<ArchitectAnnotation>();
}
