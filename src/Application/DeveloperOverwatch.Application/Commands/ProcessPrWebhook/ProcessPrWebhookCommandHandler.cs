namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ProcessPrWebhookCommandHandler
    : IRequestHandler<ProcessPrWebhookCommand, Result<int>>
{
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly IGitHostingService _gitService;
    private readonly SessionCommitCorrelator _correlator;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessPrWebhookCommandHandler(
        ICommitSessionLinkRepository linkRepo,
        IPullRequestReviewRepository prRepo,
        IGitHostingService gitService,
        SessionCommitCorrelator correlator,
        IUnitOfWork unitOfWork)
    {
        _linkRepo = linkRepo;
        _prRepo = prRepo;
        _gitService = gitService;
        _correlator = correlator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        ProcessPrWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var existingReview = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (existingReview is null)
        {
            await _prRepo.AddAsync(new PullRequestReview
            {
                Id = Guid.NewGuid().ToString(),
                Repo = request.Repo,
                PrNumber = request.PrNumber,
                Status = ReviewStatus.Pending
            }, cancellationToken);
        }

        var commits = await _gitService.GetPullRequestCommitsAsync(
            request.Repo, request.PrNumber, cancellationToken);

        var allLinks = new List<CommitSessionLink>();
        foreach (var commit in commits)
        {
            var trailerLinks = SessionCommitCorrelator.ParseTrailers(
                request.Repo, commit.Sha, commit.Message);
            allLinks.AddRange(trailerLinks);

            var correlatedLinks = await _correlator.CorrelateCommitAsync(
                request.Repo, commit.Sha, commit.CommittedAt, ct: cancellationToken);

            var trailerSessionIds = trailerLinks.Select(l => l.SessionId).ToHashSet();
            allLinks.AddRange(correlatedLinks.Where(l => !trailerSessionIds.Contains(l.SessionId)));
        }

        if (allLinks.Count > 0)
        {
            await _linkRepo.AddRangeAsync(allLinks, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(allLinks.Count);
    }
}
