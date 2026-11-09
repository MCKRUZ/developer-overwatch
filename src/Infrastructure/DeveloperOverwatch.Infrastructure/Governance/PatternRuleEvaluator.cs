namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text.RegularExpressions;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using Microsoft.Extensions.Logging;

public sealed class PatternRuleEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<PatternRuleEvaluator> _logger;

    public PatternRuleEvaluator(ILogger<PatternRuleEvaluator> logger) => _logger = logger;

    public IReadOnlyList<GovernanceViolation> Evaluate(GovernancePolicy policy, Session session)
    {
        var violations = new List<GovernanceViolation>();
        var regexes = CompilePatterns(policy.Patterns);

        if (regexes.Length == 0 && policy.Patterns.Count > 0)
            return violations;

        var excludeRegexes = CompileExcludePatterns(policy.ExcludePaths);

        foreach (var target in policy.Scan)
        {
            switch (target)
            {
                case ScanTarget.Diffs:
                    EvaluateDiffs(policy, session, regexes, excludeRegexes, violations);
                    break;
                case ScanTarget.Conversation:
                    EvaluateConversation(policy, session, regexes, violations);
                    break;
            }
        }

        return violations;
    }

    private Regex[] CompilePatterns(IReadOnlyList<string> patterns)
    {
        var compiled = new List<Regex>(patterns.Count);
        foreach (var pattern in patterns)
        {
            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern '{Pattern}', skipping", pattern);
            }
        }
        return compiled.ToArray();
    }

    private Regex[] CompileExcludePatterns(IReadOnlyList<string> excludePaths)
    {
        var compiled = new List<Regex>(excludePaths.Count);
        foreach (var pattern in excludePaths)
        {
            try
            {
                var regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*\\*", ".*")
                    .Replace("\\*", "[^/\\\\]*") + "$";
                compiled.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid exclude pattern '{Pattern}', skipping", pattern);
            }
        }
        return compiled.ToArray();
    }

    private static void EvaluateDiffs(
        GovernancePolicy policy, Session session, Regex[] regexes,
        Regex[] excludeRegexes, List<GovernanceViolation> violations)
    {
        foreach (var fileChange in session.FileChanges)
        {
            if (fileChange.DiffText is null)
                continue;
            if (IsExcluded(fileChange.FilePath, excludeRegexes))
                continue;

            foreach (var regex in regexes)
            {
                var match = regex.Match(fileChange.DiffText);
                if (match.Success)
                {
                    var violation = CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in {fileChange.FilePath}");
                    violation.ToolExecutionId = fileChange.ToolExecutionId;
                    violations.Add(violation);
                }
            }
        }
    }

    private static void EvaluateConversation(
        GovernancePolicy policy, Session session, Regex[] regexes, List<GovernanceViolation> violations)
    {
        foreach (var turn in session.ConversationTurns)
        {
            if (turn.Content is null)
                continue;

            foreach (var regex in regexes)
            {
                var match = regex.Match(turn.Content);
                if (match.Success)
                {
                    var violation = CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in conversation turn {turn.TurnIndex}");
                    violation.ConversationTurnId = turn.Id;
                    violations.Add(violation);
                }
            }
        }
    }

    private static bool IsExcluded(string filePath, Regex[] excludeRegexes)
    {
        foreach (var regex in excludeRegexes)
        {
            if (regex.IsMatch(filePath))
                return true;
        }
        return false;
    }

    private static GovernanceViolation CreateViolation(GovernancePolicy policy, string sessionId, string message) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = sessionId,
        RuleId = policy.Id,
        Severity = policy.Severity,
        Message = message,
        CreatedAt = DateTime.UtcNow
    };
}
