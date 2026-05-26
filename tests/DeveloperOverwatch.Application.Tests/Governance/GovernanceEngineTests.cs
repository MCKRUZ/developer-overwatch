namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public class GovernanceEngineTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly PatternRuleEvaluator _patternEvaluator = new(NullLogger<PatternRuleEvaluator>.Instance);
    private readonly string _policiesDir;

    public GovernanceEngineTests()
    {
        _policiesDir = Path.Combine(Path.GetTempPath(), $"overwatch-engine-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_policiesDir);
    }

    [Fact]
    public async Task EvaluateAsync_WithPatternMatch_CreatesViolation()
    {
        WritePolicyFile("no-sql", ViolationSeverity.Error, "SqlCommand");
        var session = CreateSessionWithDiff("svc.cs", "var cmd = new SqlCommand();");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _violationRepo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("no-sql");
        await _violationRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<GovernanceViolation>>(v => v.Count() == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_NoPolicies_ReturnsEmpty()
    {
        var session = CreateSessionWithDiff("svc.cs", "var cmd = new SqlCommand();");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var emptyDir = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        var config = Microsoft.Extensions.Options.Options.Create(
            new GovernanceConfig { PoliciesPath = emptyDir });
        var loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);
        var detector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
        var engine = new GovernanceEngine(
            _sessionRepo, _violationRepo, loader, _patternEvaluator, detector,
            Substitute.For<IUnitOfWork>(), NullLogger<GovernanceEngine>.Instance);

        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().BeEmpty();
        Directory.Delete(emptyDir, true);
    }

    [Fact]
    public async Task EvaluateAsync_SessionNotFound_ReturnsEmpty()
    {
        _sessionRepo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((Session?)null);

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync("nonexistent");

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_SemanticRule_SkipsWithoutError()
    {
        var yaml = """
            policies:
              - id: auth-check
                name: "Auth required"
                severity: error
                type: semantic
                scan: [diffs]
                rule: "All endpoints must have [Authorize]"
            """;
        File.WriteAllText(Path.Combine(_policiesDir, "semantic.yaml"), yaml);

        var session = CreateSessionWithDiff("ctrl.cs", "[HttpGet] public IActionResult Get()");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _violationRepo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().BeEmpty();
    }

    private GovernanceEngine CreateEngine()
    {
        var config = Microsoft.Extensions.Options.Options.Create(
            new GovernanceConfig { PoliciesPath = _policiesDir, DriftWindowHours = 72, DriftThreshold = 3 });
        var loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);
        var detector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
        return new GovernanceEngine(
            _sessionRepo, _violationRepo, loader, _patternEvaluator, detector,
            Substitute.For<IUnitOfWork>(), NullLogger<GovernanceEngine>.Instance);
    }

    private void WritePolicyFile(string ruleId, ViolationSeverity severity, string pattern)
    {
        var yaml = $"""
            policies:
              - id: {ruleId}
                name: "Test: {ruleId}"
                severity: {severity.ToString().ToLower()}
                type: pattern
                scan: [diffs]
                patterns:
                  - "{pattern}"
            """;
        File.WriteAllText(Path.Combine(_policiesDir, $"{ruleId}.yaml"), yaml);
    }

    private static Session CreateSessionWithDiff(string filePath, string diffText)
    {
        var session = new Session { Id = Guid.NewGuid().ToString(), DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        session.FileChanges.Add(new FileChange
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            FilePath = filePath,
            DiffText = diffText
        });
        return session;
    }
}
