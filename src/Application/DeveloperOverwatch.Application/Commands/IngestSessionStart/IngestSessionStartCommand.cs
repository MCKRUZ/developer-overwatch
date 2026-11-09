namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionStartCommand : IRequest<Result<string>>
{
    public required string SessionId { get; init; }
    public required string DeveloperEmail { get; init; }
    public string? DeveloperName { get; init; }
    public string? RepoPath { get; init; }
    public string? Title { get; init; }
    public required DateTime StartedAt { get; init; }

    /// <summary>Which coding agent ran the session; "claude-code" when absent.</summary>
    public string? Agent { get; init; }
    public string? MachineName { get; init; }
    public string? ProjectKey { get; init; }

    /// <summary>True when the Agent's backfill re-sends a start for a past session. Unlike a real
    /// resume, it must not reopen a session that already ended.</summary>
    public bool IsBackfill { get; init; }
}
