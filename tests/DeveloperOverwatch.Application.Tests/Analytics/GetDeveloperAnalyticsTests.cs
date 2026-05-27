namespace DeveloperOverwatch.Application.Tests.Analytics;

using DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetDeveloperAnalyticsTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly ICommitSessionLinkRepository _commitRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly GetDeveloperAnalyticsQueryHandler _handler;

    public GetDeveloperAnalyticsTests()
    {
        _handler = new GetDeveloperAnalyticsQueryHandler(_sessionRepo, _violationRepo, _commitRepo);
    }

    [Fact]
    public async Task Handle_ValidDeveloper_ReturnsMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "alice@test.com", RepoPath = "/repo/a", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "alice@test.com", RepoPath = "/repo/b", StartedAt = DateTime.UtcNow.AddDays(-2) }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Critical, Message = "crit", CreatedAt = DateTime.UtcNow },
            new() { Id = "v2", SessionId = "s1", RuleId = "r2", Severity = ViolationSeverity.Warning, Message = "warn", CreatedAt = DateTime.UtcNow },
            new() { Id = "v3", SessionId = "s2", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow }
        };
        var commits = new List<CommitSessionLink>
        {
            new() { Id = "c1", CommitSha = "abc", SessionId = "s1", Repo = "org/repo" },
            new() { Id = "c2", CommitSha = "def", SessionId = "s2", Repo = "org/repo" }
        };

        _sessionRepo.GetAllAsync(developerEmail: "alice@test.com", ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(commits.AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("alice@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var analytics = result.Value!;
        analytics.Email.Should().Be("alice@test.com");
        analytics.TotalSessions.Should().Be(2);
        analytics.TotalViolations.Should().Be(3);
        analytics.CriticalViolations.Should().Be(1);
        analytics.ErrorViolations.Should().Be(1);
        analytics.WarningViolations.Should().Be(1);
        analytics.InfoViolations.Should().Be(0);
        analytics.TotalCommits.Should().Be(2);
        analytics.RecentRepos.Should().Contain("/repo/a").And.Contain("/repo/b");
    }

    [Fact]
    public async Task Handle_NoSessions_ReturnsFail()
    {
        _sessionRepo.GetAllAsync(developerEmail: "nobody@test.com", ct: Arg.Any<CancellationToken>())
            .Returns(new List<Session>().AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("nobody@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_CalculatesWeeklySessionCount()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "dev@test.com", RepoPath = "/repo", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "dev@test.com", RepoPath = "/repo", StartedAt = DateTime.UtcNow.AddDays(-30) }
        };

        _sessionRepo.GetAllAsync(developerEmail: "dev@test.com", ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("dev@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalSessions.Should().Be(2);
        result.Value.SessionsThisWeek.Should().Be(1);
    }

    [Fact]
    public async Task Validator_EmptyEmail_Fails()
    {
        var validator = new GetDeveloperAnalyticsQueryValidator();
        var query = new GetDeveloperAnalyticsQuery("");
        var result = await validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }
}
