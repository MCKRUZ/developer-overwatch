namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

/// <summary>Per-item validity for an events batch. Each rule returns why the item is invalid, or
/// null. An invalid item is skipped and reported as rejected, so it never sinks the rest of the
/// batch. Request-level rules stay in <see cref="IngestSessionEventsCommandValidator"/>.</summary>
internal static class IngestItemRules
{
    private const int MaxNameLength = 255;
    private const int MaxToolFailureIdLength = 64;

    public static string? Turn(ConversationTurnDto t) =>
        Required(t.Id, "id") ?? Required(t.Role, "role")
        ?? AtMost(t.GitBranch, MaxNameLength, "gitBranch")
        ?? AtMost(t.SubagentName, MaxNameLength, "subagentName");

    // A tool execution references its turn, and a file change its tool execution, by foreign key.
    // A child of an item rejected in this batch would fail the whole save, so it is rejected too.
    public static string? ToolExecution(ToolExecutionDto t, IReadOnlySet<string> rejectedIds) =>
        Required(t.Id, "id") ?? Required(t.ToolName, "toolName")
        ?? AtMost(t.ToolName, MaxNameLength, "toolName")
        ?? (t.TurnId is not null && rejectedIds.Contains(t.TurnId) ? $"turn {t.TurnId} was rejected" : null);

    public static string? FileChange(FileChangeDto f, IReadOnlySet<string> rejectedIds) =>
        Required(f.Id, "id") ?? Required(f.FilePath, "filePath")
        ?? (f.ToolExecutionId is not null && rejectedIds.Contains(f.ToolExecutionId)
            ? $"tool execution {f.ToolExecutionId} was rejected"
            : null);

    public static string? ToolFailure(ToolFailureDto f) =>
        Required(f.Id, "id") ?? AtMost(f.Id, MaxToolFailureIdLength, "id")
        ?? Required(f.ToolName, "toolName") ?? AtMost(f.ToolName, MaxNameLength, "toolName")
        ?? (f.At == default ? "at is required" : null);

    /// <summary>Activity has no id to report, so an invalid event is dropped. A valid one is always
    /// accepted, with a future time clamped to now.</summary>
    public static bool IsValid(ActivityEventDto a) => Enum.IsDefined(a.State) && a.At != default;

    private static string? Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? $"{field} is required" : null;

    private static string? AtMost(string? value, int max, string field) =>
        value is not null && value.Length > max ? $"{field} exceeds {max} characters" : null;
}
