namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ReviewPullRequest;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ReviewPullRequestTests
{
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ExistingPr_UpdatesStatus()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Status = "Approved",
            ReviewedBy = "architect@team.dev",
            Notes = "Looks good"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existingReview.Status.Should().Be(ReviewStatus.Approved);
        existingReview.ReviewedBy.Should().Be("architect@team.dev");
        existingReview.Notes.Should().Be("Looks good");
        existingReview.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NonExistentPr_ReturnsFail()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 999, Arg.Any<CancellationToken>())
            .Returns((PullRequestReview?)null);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 999,
            Status = "Approved",
            ReviewedBy = "architect@team.dev"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_InvalidStatus_ReturnsFail()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Status = "InvalidStatus",
            ReviewedBy = "architect@team.dev"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
