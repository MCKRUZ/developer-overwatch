namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class GovernanceViolation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ContextJson { get; set; }

    // Anchors the violation to where it actually occurred in the timeline, so the dashboard can
    // jump straight there instead of just opening the session. At most one is set, depending on
    // which scan target (conversation vs diffs) produced the violation.
    public string? ConversationTurnId { get; set; }
    public string? ToolExecutionId { get; set; }
    public bool Resolved { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Session? Session { get; set; }
}
