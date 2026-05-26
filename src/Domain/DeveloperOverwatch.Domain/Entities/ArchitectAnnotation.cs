namespace DeveloperOverwatch.Domain.Entities;

public class ArchitectAnnotation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? TurnId { get; set; }
    public string? ToolExecutionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Session? Session { get; set; }
    public ConversationTurn? Turn { get; set; }
    public ToolExecution? ToolExecution { get; set; }
}
