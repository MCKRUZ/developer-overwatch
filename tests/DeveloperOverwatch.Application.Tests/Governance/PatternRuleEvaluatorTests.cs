namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class PatternRuleEvaluatorTests
{
    private readonly PatternRuleEvaluator _evaluator = new(NullLogger<PatternRuleEvaluator>.Instance);

    [Fact]
    public void Evaluate_PatternInDiff_ReturnsViolation()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand|SqlConnection"]);
        var session = CreateSession(
            diffs: [("src/Services/DataService.cs", "var cmd = new SqlCommand(query);")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("no-sql");
        violations[0].Severity.Should().Be(ViolationSeverity.Error);
        violations[0].Message.Should().Contain("SqlCommand");
        violations[0].Message.Should().Contain("DataService.cs");
    }

    [Fact]
    public void Evaluate_PatternInConversation_ReturnsViolation()
    {
        var policy = CreatePolicy("no-secrets", ViolationSeverity.Critical,
            [ScanTarget.Conversation], ["password\\s*=\\s*\"[^\"]+\""]);
        var session = CreateSession(
            conversations: [("assistant", "Here: password = \"hunter2\"")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().ContainSingle();
        violations[0].Message.Should().Contain("conversation turn");
    }

    [Fact]
    public void Evaluate_ExcludedPath_SkipsFile()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"],
            excludePaths: ["**/Repositories/**"]);
        var session = CreateSession(
            diffs: [("src/Repositories/UserRepo.cs", "var cmd = new SqlCommand();")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsEmpty()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"]);
        var session = CreateSession(
            diffs: [("src/Service.cs", "var result = await _db.Users.ToListAsync();")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NullDiffText_SkipsGracefully()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"]);
        var session = new Session
        {
            Id = "s1",
            FileChanges = { new FileChange { Id = "fc1", SessionId = "s1", FilePath = "test.cs", DiffText = null } }
        };

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MultiplePatterns_FindsAll()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand", "ExecuteSqlRaw"]);
        var session = CreateSession(
            diffs: [
                ("a.cs", "new SqlCommand()"),
                ("b.cs", "db.ExecuteSqlRaw(sql)")
            ]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_BothScanTargets_ScansAll()
    {
        var policy = CreatePolicy("no-secrets", ViolationSeverity.Critical,
            [ScanTarget.Diffs, ScanTarget.Conversation], ["api_key\\s*="]);
        var session = CreateSession(
            diffs: [("config.cs", "api_key = \"abc123\"")],
            conversations: [("user", "set api_key = xyz")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().HaveCount(2);
    }

    private static GovernancePolicy CreatePolicy(
        string id, ViolationSeverity severity,
        List<ScanTarget> scan, List<string> patterns,
        List<string>? excludePaths = null) => new()
    {
        Id = id,
        Name = $"Test: {id}",
        Severity = severity,
        Type = PolicyType.Pattern,
        Scan = scan,
        Patterns = patterns,
        ExcludePaths = excludePaths ?? []
    };

    private static Session CreateSession(
        (string path, string diff)[]? diffs = null,
        (string role, string content)[]? conversations = null)
    {
        var session = new Session { Id = Guid.NewGuid().ToString() };

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
