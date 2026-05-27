namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class DriftDetectorTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();

    private DriftDetector CreateDetector(int threshold = 3, double alpha = 0.3, int bucketHours = 4, int windowHours = 72)
    {
        var config = Options.Create(new GovernanceConfig
        {
            DriftWindowHours = windowHours,
            DriftThreshold = threshold,
            DriftAlpha = alpha,
            DriftBucketHours = bucketHours
        });
        return new DriftDetector(config, NullLogger<DriftDetector>.Instance);
    }

    [Fact]
    public async Task DetectAsync_RecentBurst_CreatesDriftViolation()
    {
        var detector = CreateDetector(threshold: 1);
        var currentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow)
        };

        var recentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-1)),
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-2)),
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-3))
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().ContainSingle();
        driftViolations[0].RuleId.Should().Be("drift:no-sql");
        driftViolations[0].Severity.Should().Be(ViolationSeverity.Warning);
        driftViolations[0].Message.Should().Contain("EWMA");
    }

    [Fact]
    public async Task DetectAsync_SpreadOverTime_MayNotTrigger()
    {
        var detector = CreateDetector(threshold: 3);
        var currentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow)
        };

        // Violations spread far apart — EWMA dampens them
        var recentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-24)),
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-48))
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await detector.DetectAsync("s5", currentViolations, _repo);

        // EWMA should be below threshold since violations are spread across distant buckets
        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_BelowThreshold_ReturnsEmpty()
    {
        var detector = CreateDetector(threshold: 3);
        var currentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow)
        };

        var recentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-2))
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_ResolvedViolations_NotCounted()
    {
        var detector = CreateDetector(threshold: 3);
        var currentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow)
        };

        var recentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-1), resolved: true),
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-2), resolved: true),
            CreateViolation("no-sql", DateTime.UtcNow.AddHours(-3), resolved: true)
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_NoCurrentViolations_ReturnsEmpty()
    {
        var detector = CreateDetector();
        _repo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var driftViolations = await detector.DetectAsync("s5", [], _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_HighAlpha_MoreResponsiveToRecent()
    {
        // High alpha weights recent buckets more heavily
        var highAlpha = CreateDetector(threshold: 2, alpha: 0.8, bucketHours: 1);
        var currentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow)
        };

        // 3 violations in the last hour — high alpha should amplify this
        var recentViolations = new List<GovernanceViolation>
        {
            CreateViolation("no-sql", DateTime.UtcNow.AddMinutes(-10)),
            CreateViolation("no-sql", DateTime.UtcNow.AddMinutes(-20))
        };

        _repo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await highAlpha.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().ContainSingle();
    }

    private static GovernanceViolation CreateViolation(string ruleId, DateTime createdAt, bool resolved = false) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = "session-" + Guid.NewGuid().ToString()[..8],
        RuleId = ruleId,
        Severity = ViolationSeverity.Error,
        Message = $"Test violation for {ruleId}",
        CreatedAt = createdAt,
        Resolved = resolved
    };
}
