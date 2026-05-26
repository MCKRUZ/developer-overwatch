namespace DeveloperOverwatch.Application.Queries.GetPullRequestDetail;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetPullRequestDetailQuery(string Repo, int PrNumber)
    : IRequest<Result<PullRequestDetailDto>>;

public sealed record PullRequestDetailDto
{
    public required string Id { get; init; }
    public required int PrNumber { get; init; }
    public required string Repo { get; init; }
    public required string Status { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<LinkedSessionDto> LinkedSessions { get; init; }
}

public sealed record LinkedSessionDto
{
    public required string SessionId { get; init; }
    public required string CommitSha { get; init; }
    public required double Confidence { get; init; }
    public required string LinkMethod { get; init; }
    public string? DeveloperEmail { get; init; }
    public DateTime? SessionStartedAt { get; init; }
    public DateTime? SessionEndedAt { get; init; }
}
