namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ProcessPrWebhook;
using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ProcessPrWebhookTests
{
    private readonly ICommitSessionLinkRepository _linkRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_NewPr_CreatesPullRequestReview()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPrWebhookCommandHandler(
            _linkRepo, _prRepo, _gitService, correlator, _unitOfWork);

        var commitTime = DateTime.UtcNow;
        var command = new ProcessPrWebhookCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Action = "opened"
        };

        _gitService.GetPullRequestCommitsAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(new List<CommitInfo>
            {
                new("abc123", "feat: add thing", commitTime)
            });

        _gitService.GetCommitFilesAsync("owner/repo", "abc123", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Thing.cs" });

        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>
            {
                new()
                {
                    Id = "session-1",
                    DeveloperEmail = "dev@test.com",
                    RepoPath = "owner/repo",
                    Status = SessionStatus.Completed,
                    StartedAt = commitTime.AddHours(-1),
                    EndedAt = commitTime.AddMinutes(-5),
                    FileChanges = new List<FileChange>
                    {
                        new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Thing.cs" }
                    }
                }
            });

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns((PullRequestReview?)null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _prRepo.Received(1).AddAsync(
            Arg.Is<PullRequestReview>(r => r.Repo == "owner/repo" && r.PrNumber == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingPr_DoesNotDuplicate()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPrWebhookCommandHandler(
            _linkRepo, _prRepo, _gitService, correlator, _unitOfWork);

        var command = new ProcessPrWebhookCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Action = "synchronize"
        };

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        _gitService.GetPullRequestCommitsAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(new List<CommitInfo>());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _prRepo.DidNotReceive().AddAsync(
            Arg.Any<PullRequestReview>(), Arg.Any<CancellationToken>());
    }
}
