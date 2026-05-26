namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class FileChange
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? ToolExecutionId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public string? DiffText { get; set; }
    public ChangeType? ChangeType { get; set; }

    public Session? Session { get; set; }
    public ToolExecution? ToolExecution { get; set; }
}
