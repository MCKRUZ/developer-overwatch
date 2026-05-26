namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ProcessPushWebhook;
using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ProcessPushWebhookTests
{
    private readonly ICommitSessionLinkRepository _linkRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_PushWithTrailer_CreatesTrailerLink()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPushWebhookCommandHandler(
            _linkRepo, correlator, _unitOfWork);

        var command = new ProcessPushWebhookCommand
        {
            Repo = "owner/repo",
            Commits = new[]
            {
                new PushCommitDto
                {
                    Sha = "abc123",
                    Message = "feat: add feature\n\nClaude-Session: session-42",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _gitService.GetCommitFilesAsync("owner/repo", "abc123", Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        await _linkRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<CommitSessionLink>>(links =>
                links.Any(l => l.LinkMethod == "trailer" && l.SessionId == "session-42")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PushWithFileMatch_CreatesFileMatchLink()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPushWebhookCommandHandler(
            _linkRepo, correlator, _unitOfWork);

        var commitTime = DateTime.UtcNow;
        var command = new ProcessPushWebhookCommand
        {
            Repo = "owner/repo",
            Commits = new[]
            {
                new PushCommitDto
                {
                    Sha = "def456",
                    Message = "feat: update",
                    Timestamp = commitTime
                }
            }
        };

        _gitService.GetCommitFilesAsync("owner/repo", "def456", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/File.cs" });

        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>
            {
                new()
                {
                    Id = "session-99",
                    DeveloperEmail = "dev@test.com",
                    RepoPath = "owner/repo",
                    Status = SessionStatus.Completed,
                    StartedAt = commitTime.AddHours(-1),
                    EndedAt = commitTime.AddMinutes(-10),
                    FileChanges = new List<FileChange>
                    {
                        new() { Id = "fc-1", SessionId = "session-99", FilePath = "src/File.cs" }
                    }
                }
            });

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _linkRepo.Received().AddRangeAsync(
            Arg.Is<IEnumerable<CommitSessionLink>>(links =>
                links.Any(l => l.LinkMethod == "file_match" && l.SessionId == "session-99")),
            Arg.Any<CancellationToken>());
    }
}
