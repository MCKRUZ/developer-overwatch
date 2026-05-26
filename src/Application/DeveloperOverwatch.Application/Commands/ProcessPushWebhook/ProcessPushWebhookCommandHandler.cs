namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ProcessPushWebhookCommandHandler
    : IRequestHandler<ProcessPushWebhookCommand, Result<int>>
{
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly SessionCommitCorrelator _correlator;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessPushWebhookCommandHandler(
        ICommitSessionLinkRepository linkRepo,
        SessionCommitCorrelator correlator,
        IUnitOfWork unitOfWork)
    {
        _linkRepo = linkRepo;
        _correlator = correlator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        ProcessPushWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var allLinks = new List<CommitSessionLink>();

        foreach (var commit in request.Commits)
        {
            var trailerLinks = SessionCommitCorrelator.ParseTrailers(
                request.Repo, commit.Sha, commit.Message);
            allLinks.AddRange(trailerLinks);

            var correlatedLinks = await _correlator.CorrelateCommitAsync(
                request.Repo, commit.Sha, commit.Timestamp, ct: cancellationToken);

            var trailerSessionIds = trailerLinks.Select(l => l.SessionId).ToHashSet();
            var nonDuplicateCorrelated = correlatedLinks
                .Where(l => !trailerSessionIds.Contains(l.SessionId));
            allLinks.AddRange(nonDuplicateCorrelated);
        }

        if (allLinks.Count > 0)
        {
            await _linkRepo.AddRangeAsync(allLinks, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(allLinks.Count);
    }
}
