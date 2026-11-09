namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record IngestSessionEventsCommand : IRequest<Result<int>>
{
    public required string SessionId { get; init; }
    public string? SessionTitle { get; init; }
    public IReadOnlyList<ConversationTurnDto> ConversationTurns { get; init; } = [];
    public IReadOnlyList<ToolExecutionDto> ToolExecutions { get; init; } = [];
    public IReadOnlyList<FileChangeDto> FileChanges { get; init; } = [];
    public IReadOnlyList<ActivityEventDto> ActivityEvents { get; init; } = [];
    public IReadOnlyList<ToolFailureDto> ToolFailures { get; init; } = [];
}

public sealed record ActivityEventDto
{
    public required ActivityState State { get; init; }
    public required DateTime At { get; init; }
}

/// <summary>A failed tool call. No id and no error text: the Agent ships each failure once
/// (cursor-tracked), and the server only counts it and remembers the latest tool name.</summary>
public sealed record ToolFailureDto
{
    public required string ToolName { get; init; }
    public required DateTime At { get; init; }
}

public sealed record ConversationTurnDto
{
    public required string Id { get; init; }
    public int TurnIndex { get; init; }
    public required string Role { get; init; }
    public string? Content { get; init; }
    public int? TokenCount { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CacheCreationInputTokens { get; init; }
    public int? CacheReadInputTokens { get; init; }
    public string? Model { get; init; }
    public bool ContainsRedactedSecret { get; init; }
    public required DateTime Timestamp { get; init; }
    public bool IsDecision { get; init; }
    public string? GitBranch { get; init; }
    public string? SubagentName { get; init; }
}

public sealed record ToolExecutionDto
{
    public required string Id { get; init; }
    public string? TurnId { get; init; }
    public required string ToolName { get; init; }
    public string? InputParams { get; init; }
    public string? OutputSummary { get; init; }
    public int? DurationMs { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record FileChangeDto
{
    public required string Id { get; init; }
    public string? ToolExecutionId { get; init; }
    public required string FilePath { get; init; }
    public string? BeforeHash { get; init; }
    public string? AfterHash { get; init; }
    public string? DiffText { get; init; }
    public ChangeType? ChangeType { get; init; }
}
