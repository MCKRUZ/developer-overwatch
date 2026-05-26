namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text.RegularExpressions;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;

public sealed class PatternRuleEvaluator
{
    public IReadOnlyList<GovernanceViolation> Evaluate(GovernancePolicy policy, Session session)
    {
        var violations = new List<GovernanceViolation>();
        var regexes = policy.Patterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();

        foreach (var target in policy.Scan)
        {
            switch (target)
            {
                case ScanTarget.Diffs:
                    EvaluateDiffs(policy, session, regexes, violations);
                    break;
                case ScanTarget.Conversation:
                    EvaluateConversation(policy, session, regexes, violations);
                    break;
            }
        }

        return violations;
    }

    private static void EvaluateDiffs(
        GovernancePolicy policy, Session session, Regex[] regexes, List<GovernanceViolation> violations)
    {
        foreach (var fileChange in session.FileChanges)
        {
            if (fileChange.DiffText is null)
                continue;
            if (IsExcluded(fileChange.FilePath, policy.ExcludePaths))
                continue;

            foreach (var regex in regexes)
            {
                var match = regex.Match(fileChange.DiffText);
                if (match.Success)
                {
                    violations.Add(CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in {fileChange.FilePath}"));
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
                    violations.Add(CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in conversation turn {turn.TurnIndex}"));
                }
            }
        }
    }

    private static bool IsExcluded(string filePath, IReadOnlyList<string> excludePaths)
    {
        foreach (var pattern in excludePaths)
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/\\\\]*") + "$";
            if (Regex.IsMatch(filePath, regexPattern, RegexOptions.IgnoreCase))
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
