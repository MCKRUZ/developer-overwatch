namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ReviewPullRequestCommand : IRequest<Result<string>>
{
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public required string Status { get; init; }
    public required string ReviewedBy { get; init; }
    public string? Notes { get; init; }
}
