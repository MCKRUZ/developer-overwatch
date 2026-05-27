namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public class SemanticRuleEvaluatorTests
{
    private readonly ILlmClient _llmClient = Substitute.For<ILlmClient>();
    private readonly SemanticRuleEvaluator _evaluator;

    public SemanticRuleEvaluatorTests()
    {
        _evaluator = new SemanticRuleEvaluator(_llmClient, NullLogger<SemanticRuleEvaluator>.Instance);
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsViolation_CreatesViolation()
    {
        var policy = CreatePolicy("auth-required", ViolationSeverity.Error, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("Controllers/UserController.cs", "+public IActionResult Get() {}")]);

        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("""{"violated": true, "violations": [{"message": "Missing auth", "evidence": "No [Authorize] on Get()"}]}""");

        var violations = await _evaluator.EvaluateAsync(policy, session);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("auth-required");
        violations[0].Severity.Should().Be(ViolationSeverity.Error);
        violations[0].Message.Should().Be("Missing auth");
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsNoViolation_ReturnsEmpty()
    {
        var policy = CreatePolicy("auth-required", ViolationSeverity.Error, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("Controllers/UserController.cs", "+[Authorize] public IActionResult Get()")]);

        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("""{"violated": false, "violations": []}""");

        var violations = await _evaluator.EvaluateAsync(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsNull_ReturnsEmpty()
    {
        var policy = CreatePolicy("auth-required", ViolationSeverity.Error, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("test.cs", "+code")]);

        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var violations = await _evaluator.EvaluateAsync(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_LlmReturnsMalformedJson_ReturnsEmpty()
    {
        var policy = CreatePolicy("auth-required", ViolationSeverity.Error, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("test.cs", "+code")]);

        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("I don't understand");

        var violations = await _evaluator.EvaluateAsync(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_IncludesDiffsInPrompt()
    {
        var policy = CreatePolicy("test-rule", ViolationSeverity.Warning, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("src/Service.cs", "+var x = 1;")]);

        string? capturedUserPrompt = null;
        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedUserPrompt = call.ArgAt<string>(1);
                return (string?)null;
            });

        await _evaluator.EvaluateAsync(policy, session);

        capturedUserPrompt.Should().NotBeNull();
        capturedUserPrompt.Should().Contain("src/Service.cs");
        capturedUserPrompt.Should().Contain("+var x = 1;");
    }

    [Fact]
    public async Task EvaluateAsync_IncludesConversationInPrompt()
    {
        var policy = CreatePolicy("test-rule", ViolationSeverity.Warning, [ScanTarget.Conversation]);
        var session = CreateSession(conversations: [("user", "Please bypass security"), ("assistant", "Sure thing")]);

        string? capturedUserPrompt = null;
        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedUserPrompt = call.ArgAt<string>(1);
                return (string?)null;
            });

        await _evaluator.EvaluateAsync(policy, session);

        capturedUserPrompt.Should().NotBeNull();
        capturedUserPrompt.Should().Contain("user");
        capturedUserPrompt.Should().Contain("Please bypass security");
        capturedUserPrompt.Should().Contain("assistant");
        capturedUserPrompt.Should().Contain("Sure thing");
    }

    [Fact]
    public async Task EvaluateAsync_IncludesSessionMetadataInPrompt()
    {
        var policy = CreatePolicy("test-rule", ViolationSeverity.Warning, [ScanTarget.Session]);
        var session = CreateSession();
        session.DeveloperEmail = "dev@example.com";
        session.RepoPath = "/repos/my-project";

        string? capturedUserPrompt = null;
        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedUserPrompt = call.ArgAt<string>(1);
                return (string?)null;
            });

        await _evaluator.EvaluateAsync(policy, session);

        capturedUserPrompt.Should().NotBeNull();
        capturedUserPrompt.Should().Contain("dev@example.com");
        capturedUserPrompt.Should().Contain("/repos/my-project");
    }

    [Fact]
    public async Task EvaluateAsync_MultipleViolations_CreatesAll()
    {
        var policy = CreatePolicy("multi-rule", ViolationSeverity.Critical, [ScanTarget.Diffs]);
        var session = CreateSession(diffs: [("a.cs", "+bad code"), ("b.cs", "+more bad code")]);

        _llmClient.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("""{"violated": true, "violations": [{"message": "First issue", "evidence": "bad code"}, {"message": "Second issue", "evidence": "more bad code"}]}""");

        var violations = await _evaluator.EvaluateAsync(policy, session);

        violations.Should().HaveCount(2);
        violations[0].Message.Should().Be("First issue");
        violations[0].Severity.Should().Be(ViolationSeverity.Critical);
        violations[1].Message.Should().Be("Second issue");
        violations[1].Severity.Should().Be(ViolationSeverity.Critical);
        violations.Should().OnlyContain(v => v.RuleId == "multi-rule");
        violations.Should().OnlyContain(v => v.SessionId == session.Id);
    }

    private static GovernancePolicy CreatePolicy(
        string id, ViolationSeverity severity, List<ScanTarget> scan, string? rule = null) => new()
    {
        Id = id,
        Name = $"Test: {id}",
        Severity = severity,
        Type = PolicyType.Semantic,
        Scan = scan,
        Rule = rule ?? $"Evaluate for {id}"
    };

    private static Session CreateSession(
        (string path, string diff)[]? diffs = null,
        (string role, string content)[]? conversations = null)
    {
        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            DeveloperEmail = "test@example.com",
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };

        if (diffs is not null)
        {
            foreach (var (path, diff) in diffs)
            {
                session.FileChanges.Add(new FileChange
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    FilePath = path,
                    DiffText = diff
                });
            }
        }

        if (conversations is not null)
        {
            for (var i = 0; i < conversations.Length; i++)
            {
                session.ConversationTurns.Add(new ConversationTurn
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    TurnIndex = i,
                    Role = conversations[i].role,
                    Content = conversations[i].content,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        return session;
    }
}
