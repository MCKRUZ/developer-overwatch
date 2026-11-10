namespace DeveloperOverwatch.Domain.Entities;

/// <summary>One failed tool call, keyed by the hook event id the Agent sends, so a redelivered
/// batch is recognised and counted once. <see cref="Session.ToolErrorCount"/> caches how many of
/// these a session has.</summary>
public class SessionToolFailure
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public DateTime At { get; set; }
}
