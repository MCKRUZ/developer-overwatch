namespace DeveloperOverwatch.Application.Queries.GetPullRequestDetail;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPullRequestDetailQueryHandler
    : IRequestHandler<GetPullRequestDetailQuery, Result<PullRequestDetailDto>>
{
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly IGitHostingService _gitService;

    public GetPullRequestDetailQueryHandler(
        IPullRequestReviewRepository prRepo,
        ICommitSessionLinkRepository linkRepo,
        IGitHostingService gitService)
    {
        _prRepo = prRepo;
        _linkRepo = linkRepo;
        _gitService = gitService;
    }

    public async Task<Result<PullRequestDetailDto>> Handle(
        GetPullRequestDetailQuery request,
        CancellationToken cancellationToken)
    {
        var review = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (review is null)
            return Result<PullRequestDetailDto>.Fail(
                $"PR {request.PrNumber} not found for repo {request.Repo}");

        var commits = await _gitService.GetPullRequestCommitsAsync(
            request.Repo, request.PrNumber, cancellationToken);

        var linkedSessions = new List<LinkedSessionDto>();
        foreach (var commit in commits)
        {
            var links = await _linkRepo.GetByCommitShaAsync(commit.Sha, cancellationToken);
            foreach (var link in links)
            {
                linkedSessions.Add(new LinkedSessionDto
                {
                    SessionId = link.SessionId,
                    CommitSha = link.CommitSha,
                    Confidence = link.Confidence ?? 0,
                    LinkMethod = link.LinkMethod ?? "unknown",
                    DeveloperEmail = link.Session?.DeveloperEmail,
                    SessionStartedAt = link.Session?.StartedAt,
                    SessionEndedAt = link.Session?.EndedAt
                });
            }
        }

        var dto = new PullRequestDetailDto
        {
            Id = review.Id,
            PrNumber = review.PrNumber,
            Repo = review.Repo,
            Status = review.Status.ToString(),
            ReviewedBy = review.ReviewedBy,
            ReviewedAt = review.ReviewedAt,
            Notes = review.Notes,
            LinkedSessions = linkedSessions
        };

        return Result<PullRequestDetailDto>.Success(dto);
    }
}
