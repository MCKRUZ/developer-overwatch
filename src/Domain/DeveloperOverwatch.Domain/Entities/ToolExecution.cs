namespace DeveloperOverwatch.Domain.Entities;

public class ToolExecution
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? TurnId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? InputParams { get; set; }
    public string? OutputSummary { get; set; }
    public int? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }

    public Session? Session { get; set; }
    public ConversationTurn? Turn { get; set; }
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
}
