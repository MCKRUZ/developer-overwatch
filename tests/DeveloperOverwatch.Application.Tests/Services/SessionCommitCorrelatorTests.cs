namespace DeveloperOverwatch.Application.Tests.Services;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class SessionCommitCorrelatorTests
{
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly SessionCommitCorrelator _sut;

    public SessionCommitCorrelatorTests()
    {
        _sut = new SessionCommitCorrelator(_gitService, _sessionRepo);
    }

    [Fact]
    public async Task CorrelateCommit_MatchingFiles_ReturnsLinkWithConfidence()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;
        var commitFiles = new List<string> { "src/Program.cs", "src/Startup.cs" };

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(commitFiles);

        var session = new Session
        {
            Id = "session-1",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-30),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Program.cs" },
                new() { Id = "fc-2", SessionId = "session-1", FilePath = "src/Other.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().HaveCount(1);
        links[0].CommitSha.Should().Be(commitSha);
        links[0].SessionId.Should().Be("session-1");
        links[0].Confidence.Should().Be(0.5); // 1 matched / 2 commit files
        links[0].LinkMethod.Should().Be("file_match");
    }

    [Fact]
    public async Task CorrelateCommit_NoMatchingFiles_ReturnsEmpty()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Unrelated.cs" });

        var session = new Session
        {
            Id = "session-1",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-30),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Program.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelateCommit_SessionOutsideTimeWindow_Excluded()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Program.cs" });

        var session = new Session
        {
            Id = "session-old",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddDays(-2),
            EndedAt = commitTime.AddDays(-2).AddHours(1),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-old", FilePath = "src/Program.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelateCommit_PerfectMatch_ReturnsConfidence1()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Program.cs", "src/Startup.cs" });

        var session = new Session
        {
            Id = "session-perfect",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-10),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-perfect", FilePath = "src/Program.cs" },
                new() { Id = "fc-2", SessionId = "session-perfect", FilePath = "src/Startup.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().HaveCount(1);
        links[0].Confidence.Should().Be(1.0);
    }
}
