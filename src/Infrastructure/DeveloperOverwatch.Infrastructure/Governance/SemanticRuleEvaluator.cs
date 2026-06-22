namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Evaluates a semantic governance rule with an LLM. By default a single judge runs (behavior
/// unchanged); when <see cref="GovernanceConfig.SemanticJurySize"/> &gt; 1 or
/// <see cref="GovernanceConfig.SemanticJuryPersonas"/> is set, a panel of independent judges
/// votes and only a consensus violation is recorded — cutting any one judge's false positives.
/// </summary>
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
    private readonly IOptions<GovernanceConfig> _config;
    private readonly ILogger<SemanticRuleEvaluator> _logger;

    public SemanticRuleEvaluator(
        ILlmClient llmClient,
        IOptions<GovernanceConfig> config,
        ILogger<SemanticRuleEvaluator> logger)
    {
        _llmClient = llmClient;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(
        GovernancePolicy policy, Session session, CancellationToken ct = default)
    {
        var cfg = _config.Value;
        var personas = cfg.SemanticJuryPersonas;
        var hasPersonas = personas is { Count: > 0 };
        var panelSize = hasPersonas ? personas!.Count : Math.Max(1, cfg.SemanticJurySize);

        var userPrompt = BuildUserPrompt(policy, session);

        return panelSize <= 1
            ? await EvaluateSingleAsync(policy, session.Id, userPrompt, hasPersonas ? personas![0] : null, ct)
            : await EvaluatePanelAsync(
                // Pass null (not an empty list) when there are no personas, so the panelist loop
                // never indexes past the end — panelSize then comes from SemanticJurySize.
                policy, session.Id, userPrompt, hasPersonas ? personas : null, panelSize,
                cfg.SemanticConsensusThreshold, ct);
    }

    private async Task<IReadOnlyList<GovernanceViolation>> EvaluateSingleAsync(
        GovernancePolicy policy, string sessionId, string userPrompt, string? persona, CancellationToken ct)
    {
        string? response;
        try
        {
            response = await _llmClient.CompleteAsync(BuildSystemPrompt(persona), userPrompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM call failed for policy {PolicyId} on session {SessionId}",
                policy.Id, sessionId);
            return [];
        }

        if (response is null)
            return [];

        return ParseResponse(response, policy, sessionId);
    }

    private async Task<IReadOnlyList<GovernanceViolation>> EvaluatePanelAsync(
        GovernancePolicy policy, string sessionId, string userPrompt,
        IReadOnlyList<string>? personas, int panelSize, double threshold, CancellationToken ct)
    {
        var tasks = new Task<PanelistVerdict>[panelSize];
        for (var i = 0; i < panelSize; i++)
        {
            var persona = personas is not null ? personas[i] : null;
            tasks[i] = RunPanelistAsync(policy, userPrompt, persona, ct);
        }

        var verdicts = await Task.WhenAll(tasks);
        var consensus = JuryVoteAggregator.Aggregate(verdicts, threshold);

        _logger.LogInformation(
            "Semantic jury for policy {PolicyId}: {Bucket} — {Votes}/{Responded} of {Panel} judges flagged a violation",
            policy.Id, consensus.Bucket, consensus.ViolatedVotes, consensus.Responded, consensus.Panelists);

        if (!consensus.Violated)
            return [];

        return consensus.Findings
            .Where(f => !string.IsNullOrWhiteSpace(f.Message))
            .Select(f => BuildViolation(policy, sessionId, f.Message, JsonSerializer.Serialize(
                new
                {
                    f.Evidence,
                    Consensus = consensus.Bucket.ToString(),
                    Votes = consensus.ViolatedVotes,
                    Responded = consensus.Responded,
                    Panelists = consensus.Panelists
                },
                JsonOptions)))
            .ToArray();
    }

    private static GovernanceViolation BuildViolation(
        GovernancePolicy policy, string sessionId, string message, string contextJson) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = sessionId,
        RuleId = policy.Id,
        Severity = policy.Severity,
        Message = message,
        ContextJson = contextJson,
        CreatedAt = DateTime.UtcNow
    };

    private async Task<PanelistVerdict> RunPanelistAsync(
        GovernancePolicy policy, string userPrompt, string? persona, CancellationToken ct)
    {
        string? response;
        try
        {
            response = await _llmClient.CompleteAsync(BuildSystemPrompt(persona), userPrompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Jury panelist LLM call failed for policy {PolicyId}", policy.Id);
            return new PanelistVerdict(false, false, []);
        }

        if (response is null)
            return new PanelistVerdict(false, false, []);

        var audit = TryParseAudit(response, policy.Id);
        if (audit is null)
            return new PanelistVerdict(false, false, []);

        var findings = audit.Violations
            .Where(v => !string.IsNullOrWhiteSpace(v.Message))
            .Select(v => new SemanticFinding(v.Message, v.Evidence))
            .ToArray();

        return new PanelistVerdict(true, audit.Violated && findings.Length > 0, findings);
    }

    private static string BuildSystemPrompt(string? persona) =>
        string.IsNullOrWhiteSpace(persona)
            ? SystemPrompt
            : $"{SystemPrompt}\n\nReview from this perspective: {persona}";

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
        var result = TryParseAudit(response, policy.Id);

        if (result is not { Violated: true, Violations.Count: > 0 })
            return [];

        var violations = new List<GovernanceViolation>(result.Violations.Count);

        foreach (var v in result.Violations)
        {
            if (string.IsNullOrWhiteSpace(v.Message))
                continue;

            violations.Add(BuildViolation(
                policy, sessionId, v.Message, JsonSerializer.Serialize(new { v.Evidence }, JsonOptions)));
        }

        return violations;
    }

    private LlmAuditResponse? TryParseAudit(string response, string policyId)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                _logger.LogWarning("LLM response for policy {PolicyId} contained no JSON object", policyId);
                return null;
            }

            var jsonSpan = response[jsonStart..(jsonEnd + 1)];
            return JsonSerializer.Deserialize<LlmAuditResponse>(jsonSpan, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response for policy {PolicyId}", policyId);
            return null;
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
