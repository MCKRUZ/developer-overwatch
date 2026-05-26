namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ReviewPullRequestCommandHandler
    : IRequestHandler<ReviewPullRequestCommand, Result<string>>
{
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly IUnitOfWork _unitOfWork;

    public ReviewPullRequestCommandHandler(
        IPullRequestReviewRepository prRepo,
        IUnitOfWork unitOfWork)
    {
        _prRepo = prRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        ReviewPullRequestCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReviewStatus>(request.Status, ignoreCase: true, out var status))
            return Result<string>.Fail($"Invalid review status: {request.Status}");

        var review = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (review is null)
            return Result<string>.Fail(
                $"PR {request.PrNumber} not found for repo {request.Repo}");

        review.Status = status;
        review.ReviewedBy = request.ReviewedBy;
        review.ReviewedAt = DateTime.UtcNow;
        review.Notes = request.Notes;

        await _prRepo.UpdateAsync(review, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(review.Id);
    }
}
