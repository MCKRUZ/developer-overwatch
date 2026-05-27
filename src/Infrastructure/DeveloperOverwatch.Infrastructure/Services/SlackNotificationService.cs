namespace DeveloperOverwatch.Infrastructure.Services;

using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class SlackNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<SlackConfig> _config;
    private readonly ILogger<SlackNotificationService> _logger;

    private static readonly IReadOnlyDictionary<string, string> SeverityColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Critical"] = "#dc3545",
            ["Error"] = "#fd7e14",
            ["Warning"] = "#ffc107",
            ["Info"] = "#17a2b8"
        };

    public SlackNotificationService(
        HttpClient httpClient,
        IOptionsMonitor<SlackConfig> config,
        ILogger<SlackNotificationService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task NotifyViolationAsync(
        string ruleId,
        string severity,
        string message,
        string sessionId,
        string developerEmail,
        CancellationToken ct = default)
    {
        var cfg = _config.CurrentValue;

        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.WebhookUrl))
        {
            _logger.LogDebug("Slack notifications disabled or webhook URL not configured");
            return;
        }

        if (!MeetsSeverityThreshold(severity, cfg.MinimumSeverity))
            return;

        var color = SeverityColors.GetValueOrDefault(severity, "#808080");
        var truncatedSession = sessionId.Length > 8 ? sessionId[..8] : sessionId;

        var payload = new
        {
            attachments = new[]
            {
                new
                {
                    color,
                    fallback = $"[{severity}] Governance violation: {ruleId} — {message}",
                    title = "Governance Violation Detected",
                    text = message,
                    fields = new[]
                    {
                        new { title = "Rule ID", value = ruleId, @short = true },
                        new { title = "Severity", value = severity, @short = true },
                        new { title = "Developer", value = developerEmail, @short = true },
                        new { title = "Session", value = truncatedSession, @short = true }
                    },
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(cfg.WebhookUrl, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Slack notification failed with status {StatusCode} for rule {RuleId}",
                response.StatusCode, ruleId);
        }
    }

    internal static bool MeetsSeverityThreshold(string severity, string minimumSeverity)
    {
        if (!Enum.TryParse<ViolationSeverity>(severity, ignoreCase: true, out var sev))
            return false;
        if (!Enum.TryParse<ViolationSeverity>(minimumSeverity, ignoreCase: true, out var min))
            return false;
        return sev >= min;
    }
}
