namespace DeveloperOverwatch.Application.Tests.Analytics;

using DeveloperOverwatch.Application.Queries.GetTeamAnalytics;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetTeamAnalyticsTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly ICommitSessionLinkRepository _commitRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly ICostThresholdProvider _costThreshold = Substitute.For<ICostThresholdProvider>();
    private readonly GetTeamAnalyticsQueryHandler _handler;

    public GetTeamAnalyticsTests()
    {
        _sessionRepo.GetTurnUsageRowsForDateRangeAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<TurnUsageRow>());
        _costThreshold.DailyThresholdUsd.Returns(50.00m);
        _handler = new GetTeamAnalyticsQueryHandler(_sessionRepo, _prRepo, _violationRepo, _commitRepo, _costThreshold);
    }

    [Fact]
    public async Task Handle_ReturnsAggregatedMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "alice@test.com", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "bob@test.com", StartedAt = DateTime.UtcNow.AddDays(-2) }
        };
        var prs = new List<PullRequestReview>
        {
            new() { Id = "pr1", PrNumber = 1, Repo = "org/repo", Status = ReviewStatus.Approved },
            new() { Id = "pr2", PrNumber = 2, Repo = "org/repo", Status = ReviewStatus.Pending }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow.AddDays(-1) }
        };
        var commits = new List<CommitSessionLink>
        {
            new() { Id = "c1", CommitSha = "abc123", SessionId = "s1", Repo = "org/repo" },
            new() { Id = "c2", CommitSha = "def456", SessionId = "s1", Repo = "org/repo" }
        };

        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(prs.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(commits.AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var analytics = result.Value!;
        analytics.TotalSessions.Should().Be(2);
        analytics.TotalPullRequests.Should().Be(2);
        analytics.ApprovedPullRequests.Should().Be(1);
        analytics.PendingPullRequests.Should().Be(1);
        analytics.TotalViolations.Should().Be(1);
        analytics.Developers.Should().HaveCount(2);

        var alice = analytics.Developers.Single(d => d.Email == "alice@test.com");
        alice.SessionCount.Should().Be(1);
        alice.ViolationCount.Should().Be(1);
        alice.CommitCount.Should().Be(2);

        var bob = analytics.Developers.Single(d => d.Email == "bob@test.com");
        bob.SessionCount.Should().Be(1);
        bob.ViolationCount.Should().Be(0);
        bob.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ComputesDailyCostTrendAndExposesThreshold()
    {
        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<Session>().AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var day1 = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);
        _sessionRepo.GetTurnUsageRowsForDateRangeAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<TurnUsageRow>
            {
                new(day1, "claude-sonnet-5", 100, 50, 0, 0, 1.00m),
                new(day1, "claude-sonnet-5", 100, 50, 0, 0, 1.00m),
                new(day2, "claude-opus", 100, 50, 0, 0, 5.00m),
            });
        _costThreshold.DailyThresholdUsd.Returns(3.00m);

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var analytics = result.Value!;
        analytics.DailyCostThresholdUsd.Should().Be(3.00m);
        analytics.DailyCostTrend.Should().HaveCount(2);
        analytics.DailyCostTrend[0].Date.Should().Be(DateOnly.FromDateTime(day1));
        analytics.DailyCostTrend[0].EstimatedCostUsd.Should().Be(2.00m);
        analytics.DailyCostTrend[1].EstimatedCostUsd.Should().Be(5.00m);
    }

    [Fact]
    public async Task Handle_NoData_ReturnsZeroCounts()
    {
        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<Session>().AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalSessions.Should().Be(0);
        result.Value.Developers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CalculatesWeeklyMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow.AddDays(-30) }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "v2", SessionId = "s2", RuleId = "r2", Severity = ViolationSeverity.Info, Message = "old", CreatedAt = DateTime.UtcNow.AddDays(-30) }
        };

        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SessionsThisWeek.Should().Be(1);
        result.Value.ViolationsThisWeek.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithDeveloperFilter_ForwardsToSessionAndUsageRowQueries()
    {
        _sessionRepo.GetAllAsync(developerEmail: "alice@test.com", ct: Arg.Any<CancellationToken>())
            .Returns(new List<Session>().AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(
            new GetTeamAnalyticsQuery(Developer: "alice@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _sessionRepo.Received(1).GetAllAsync(
            developerEmail: "alice@test.com", ct: Arg.Any<CancellationToken>());
        await _sessionRepo.Received(1).GetTurnUsageRowsForDateRangeAsync(
            null, null, "alice@test.com", Arg.Any<CancellationToken>());
    }
}
