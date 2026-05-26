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
    private readonly DriftDetector _detector;

    public DriftDetectorTests()
    {
        var config = Options.Create(new GovernanceConfig { DriftWindowHours = 72, DriftThreshold = 3 });
        _detector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
    }

    [Fact]
    public async Task DetectAsync_RepeatedRule_CreatesDriftViolation()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new() { Id = "v2", SessionId = "s2", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old2", CreatedAt = DateTime.UtcNow.AddHours(-5) },
            new() { Id = "v3", SessionId = "s3", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old3", CreatedAt = DateTime.UtcNow.AddHours(-10) }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().ContainSingle();
        driftViolations[0].RuleId.Should().Be("drift:no-sql");
        driftViolations[0].Severity.Should().Be(ViolationSeverity.Warning);
        driftViolations[0].Message.Should().Contain("no-sql");
        driftViolations[0].Message.Should().Contain("4");
    }

    [Fact]
    public async Task DetectAsync_BelowThreshold_ReturnsEmpty()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", CreatedAt = DateTime.UtcNow.AddHours(-2) }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_ResolvedViolations_NotCounted()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", Resolved = true },
            new() { Id = "v2", SessionId = "s2", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old2", Resolved = true },
            new() { Id = "v3", SessionId = "s3", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old3", Resolved = true }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_NoCurrentViolations_ReturnsEmpty()
    {
        _repo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var driftViolations = await _detector.DetectAsync("s5", [], _repo);

        driftViolations.Should().BeEmpty();
    }
}
