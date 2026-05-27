namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using Microsoft.Extensions.Logging;

public sealed class SemanticRuleEvaluator
{
    private const int MaxFilesPerEvaluation = 20;
    private const int MaxDiffCharsPerFile = 500;
    private const int MaxConversationTurns = 30;
    private const int MaxCharsPerTurn = 300;

    private static readonly string SystemPrompt = """
        You are a code governance auditor. You evaluate development sessions against governance rules.
        Respond ONLY with a JSON object in this exact format:
        {"violated": true/false, "violations": [{"message": "description of violation", "evidence": "specific evidence from the context"}]}
        If the rule is not violated, respond: {"violated": false, "violations": []}
        Be precise. Only flag clear violations, not edge cases.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmClient _llmClient;
    private readonly ILogger<SemanticRuleEvaluator> _logger;

    public SemanticRuleEvaluator(ILlmClient llmClient, ILogger<SemanticRuleEvaluator> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(
        GovernancePolicy policy, Session session, CancellationToken ct = default)
    {
        var userPrompt = BuildUserPrompt(policy, session);

        string? response;
        try
        {
            response = await _llmClient.CompleteAsync(SystemPrompt, userPrompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM call failed for policy {PolicyId} on session {SessionId}",
                policy.Id, session.Id);
            return [];
        }

        if (response is null)
            return [];

        return ParseResponse(response, policy, session.Id);
    }

    private static string BuildUserPrompt(GovernancePolicy policy, Session session)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Governance Rule");
        sb.AppendLine(policy.Rule ?? policy.Name);
        sb.AppendLine();

        foreach (var target in policy.Scan)
        {
            switch (target)
            {
                case ScanTarget.Diffs:
                    AppendDiffs(sb, session);
                    break;
                case ScanTarget.Conversation:
                    AppendConversation(sb, session);
                    break;
                case ScanTarget.Session:
                    AppendSessionMetadata(sb, session);
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AppendDiffs(StringBuilder sb, Session session)
    {
        var changes = session.FileChanges;
        if (changes.Count == 0)
            return;

        sb.AppendLine("## File Changes");

        var count = Math.Min(changes.Count, MaxFilesPerEvaluation);
        for (var i = 0; i < count; i++)
        {
            var change = changes[i];
            sb.Append("### ").AppendLine(change.FilePath);

            if (change.DiffText is not null)
            {
                var truncated = change.DiffText.Length > MaxDiffCharsPerFile
                    ? change.DiffText[..MaxDiffCharsPerFile] + "...[truncated]"
                    : change.DiffText;
                sb.AppendLine(truncated);
            }

            sb.AppendLine();
        }

        if (changes.Count > MaxFilesPerEvaluation)
            sb.AppendLine($"[{changes.Count - MaxFilesPerEvaluation} additional files omitted]");
    }

    private static void AppendConversation(StringBuilder sb, Session session)
    {
        var turns = session.ConversationTurns;
        if (turns.Count == 0)
            return;

        sb.AppendLine("## Conversation");

        var count = Math.Min(turns.Count, MaxConversationTurns);
        for (var i = 0; i < count; i++)
        {
            var turn = turns[i];
            sb.Append('[').Append(turn.Role).Append("]: ");

            if (turn.Content is not null)
            {
                var truncated = turn.Content.Length > MaxCharsPerTurn
                    ? turn.Content[..MaxCharsPerTurn] + "...[truncated]"
                    : turn.Content;
                sb.AppendLine(truncated);
            }
            else
            {
                sb.AppendLine("[no content]");
            }
        }

        if (turns.Count > MaxConversationTurns)
            sb.AppendLine($"[{turns.Count - MaxConversationTurns} additional turns omitted]");

        sb.AppendLine();
    }

    private static void AppendSessionMetadata(StringBuilder sb, Session session)
    {
        sb.AppendLine("## Session Metadata");
        sb.Append("Developer: ").AppendLine(session.DeveloperEmail);
        sb.Append("Repository: ").AppendLine(session.RepoPath ?? "unknown");
        sb.Append("Started: ").AppendLine(session.StartedAt.ToString("O"));

        if (session.EndedAt.HasValue)
        {
            var duration = session.EndedAt.Value - session.StartedAt;
            sb.Append("Duration: ").Append(duration.TotalMinutes.ToString("F1")).AppendLine(" minutes");
        }

        sb.Append("Tool executions: ").AppendLine(session.ToolExecutions.Count.ToString());
        sb.Append("Files changed: ").AppendLine(session.FileChanges.Count.ToString());
        sb.AppendLine();
    }

    private IReadOnlyList<GovernanceViolation> ParseResponse(string response, GovernancePolicy policy, string sessionId)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                _logger.LogWarning("LLM response for policy {PolicyId} contained no JSON object", policy.Id);
                return [];
            }

            var jsonSpan = response[jsonStart..(jsonEnd + 1)];
            var result = JsonSerializer.Deserialize<LlmAuditResponse>(jsonSpan, JsonOptions);

            if (result is not { Violated: true, Violations.Count: > 0 })
                return [];

            var violations = new List<GovernanceViolation>(result.Violations.Count);

            foreach (var v in result.Violations)
            {
                if (string.IsNullOrWhiteSpace(v.Message))
                    continue;

                violations.Add(new GovernanceViolation
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    RuleId = policy.Id,
                    Severity = policy.Severity,
                    Message = v.Message,
                    ContextJson = JsonSerializer.Serialize(
                        new { v.Evidence }, JsonOptions),
                    CreatedAt = DateTime.UtcNow
                });
            }

            return violations;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response for policy {PolicyId}", policy.Id);
            return [];
        }
    }

    private sealed class LlmAuditResponse
    {
        public bool Violated { get; init; }
        public List<LlmViolation> Violations { get; init; } = [];
    }

    private sealed class LlmViolation
    {
        public string Message { get; init; } = string.Empty;
        public string? Evidence { get; init; }
    }
}
