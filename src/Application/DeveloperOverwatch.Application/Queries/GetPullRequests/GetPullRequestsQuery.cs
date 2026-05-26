namespace DeveloperOverwatch.Application.Queries.GetPullRequests;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetPullRequestsQuery(string? Repo = null)
    : IRequest<Result<IReadOnlyList<PullRequestReview>>>;
