namespace DeveloperOverwatch.Application.Queries.GetPullRequests;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPullRequestsQueryHandler
    : IRequestHandler<GetPullRequestsQuery, Result<IReadOnlyList<PullRequestReview>>>
{
    private readonly IPullRequestReviewRepository _repository;

    public GetPullRequestsQueryHandler(IPullRequestReviewRepository repository) =>
        _repository = repository;

    public async Task<Result<IReadOnlyList<PullRequestReview>>> Handle(
        GetPullRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var reviews = await _repository.GetAllAsync(request.Repo, cancellationToken);
        return Result<IReadOnlyList<PullRequestReview>>.Success(reviews);
    }
}
