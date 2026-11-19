namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Enums;

/// <summary>What the session's context window held when the Agent measured it. The Agent sends one only
/// on the first request of a ship, and resends the same turns without it if the request is refused, so
/// <see cref="ContextSnapshotDtoValidator"/> may reject the whole request over it.</summary>
public sealed record ContextSnapshotDto
{
    public DateTime TakenAt { get; init; }
    public string? ClaudeCodeVersion { get; init; }
    public string? Model { get; init; }
    public long MeasuredContextTokens { get; init; }
    public IReadOnlyList<ContextCategoryDto> Categories { get; init; } = [];
    public IReadOnlyList<ContextToolGroupDto> Tools { get; init; } = [];
    public IReadOnlyList<ContextCompactionDto> Compactions { get; init; } = [];
}

public sealed record ContextCategoryDto
{
    public ContextCategory Category { get; init; }
    public string? Server { get; init; }
    public long Tokens { get; init; }
}

public sealed record ContextToolGroupDto
{
    public string? Group { get; init; }
    public bool IsMcp { get; init; }
    public int ToolCount { get; init; }
    public long TokensPerRequest { get; init; }
    public bool Used { get; init; }
}

public sealed record ContextCompactionDto
{
    public DateTime At { get; init; }
    public string? Trigger { get; init; }
    public long PreTokens { get; init; }
    public long PostTokens { get; init; }
}
