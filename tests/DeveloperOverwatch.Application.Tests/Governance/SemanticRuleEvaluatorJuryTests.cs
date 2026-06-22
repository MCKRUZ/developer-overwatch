namespace DeveloperOverwatch.Application.Tests.Governance;

using System.Collections.Concurrent;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class SemanticRuleEvaluatorJuryTests
{
    private const string ViolatedJson =
        """{"violated": true, "violations": [{"message": "Missing auth", "evidence": "no [Authorize]"}]}""";
    private const string CleanJson = """{"violated": false, "violations": []}""";

    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();

    private SemanticRuleEvaluator CreateEvaluator(GovernanceConfig config) =>
        new(_llm, Options.Create(config), NullLogger<SemanticRuleEvaluator>.Instance);

    private void Responses(params string?[] responses)
    {
        var queue = new ConcurrentQueue<string?>(responses);
        _llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => queue.TryDequeue(out var r) ? r : CleanJson);
    }

    [Fact]
    public async Task Panel_MajorityFlagViolation_RecordsIt()
    {
        Responses(ViolatedJson, ViolatedJson, CleanJson);
        var evaluator = CreateEvaluator(new GovernanceConfig { SemanticJurySize = 3 });

        var violations = await evaluator.EvaluateAsync(Policy(), Session());

        violations.Should().ContainSingle();
        violations[0].Message.Should().Be("Missing auth");
        await _llm.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Panel_MinorityFlagViolation_Suppressed()
    {
        Responses(ViolatedJson, CleanJson, CleanJson);
        var evaluator = CreateEvaluator(new GovernanceConfig { SemanticJurySize = 3 });

        var violations = await evaluator.EvaluateAsync(Policy(), Session());

        violations.Should().BeEmpty("a single over-eager judge cannot create a false positive");
    }

    [Fact]
    public async Task Panel_AllJudgesFail_ReturnsEmpty()
    {
        Responses(null, null, null);
        var evaluator = CreateEvaluator(new GovernanceConfig { SemanticJurySize = 3 });

        var violations = await evaluator.EvaluateAsync(Policy(), Session());

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_RecordsConsensusMetadataInContext()
    {
        Responses(ViolatedJson, ViolatedJson, ViolatedJson);
        var evaluator = CreateEvaluator(new GovernanceConfig { SemanticJurySize = 3 });

        var violations = await evaluator.EvaluateAsync(Policy(), Session());

        violations.Should().ContainSingle();
        violations[0].ContextJson.Should().Contain("Consensus");
    }

    [Fact]
    public async Task Panel_EmptyPersonasListWithJurySize_DoesNotThrow_RunsIdenticalJudges()
    {
        // Regression: an empty (non-null) persona list must fall back to JurySize identical
        // judges, not index past the end of the empty list.
        Responses(ViolatedJson, ViolatedJson, ViolatedJson);
        var config = new GovernanceConfig { SemanticJurySize = 3, SemanticJuryPersonas = [] };
        var evaluator = CreateEvaluator(config);

        var violations = await evaluator.EvaluateAsync(Policy(), Session());

        violations.Should().ContainSingle();
        await _llm.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Personas_DrivePanelSize_AndAppearInSystemPrompt()
    {
        var systemPrompts = new ConcurrentBag<string>();
        _llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { systemPrompts.Add(call.ArgAt<string>(0)); return CleanJson; });

        var config = new GovernanceConfig
        {
            SemanticJurySize = 1, // ignored when personas are present
            SemanticJuryPersonas = ["a security reviewer", "a performance reviewer"]
        };
        var evaluator = CreateEvaluator(config);

        await evaluator.EvaluateAsync(Policy(), Session());

        systemPrompts.Should().HaveCount(2, "one judge per persona");
        systemPrompts.Should().Contain(p => p.Contains("a security reviewer"));
        systemPrompts.Should().Contain(p => p.Contains("a performance reviewer"));
    }

    private static GovernancePolicy Policy() => new()
    {
        Id = "auth-required",
        Name = "Auth required",
        Severity = ViolationSeverity.Error,
        Type = PolicyType.Semantic,
        Scan = [ScanTarget.Diffs],
        Rule = "All endpoints must require authorization"
    };

    private static Session Session()
    {
        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            DeveloperEmail = "dev@example.com",
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        session.FileChanges.Add(new FileChange
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            FilePath = "Controllers/UserController.cs",
            DiffText = "+public IActionResult Get() {}"
        });
        return session;
    }
}
