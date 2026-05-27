namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Application.Interfaces;
using Microsoft.Extensions.Logging;

public sealed class CompositeNotificationService : INotificationService
{
    private readonly SlackNotificationService _slack;
    private readonly EmailNotificationService _email;
    private readonly ILogger<CompositeNotificationService> _logger;

    public CompositeNotificationService(
        SlackNotificationService slack,
        EmailNotificationService email,
        ILogger<CompositeNotificationService> logger)
    {
        _slack = slack;
        _email = email;
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
        var tasks = new[]
        {
            SafeNotifyAsync(_slack, "Slack", ruleId, severity, message, sessionId, developerEmail, ct),
            SafeNotifyAsync(_email, "Email", ruleId, severity, message, sessionId, developerEmail, ct)
        };

        await Task.WhenAll(tasks);
    }

    private async Task SafeNotifyAsync(
        INotificationService channel,
        string channelName,
        string ruleId,
        string severity,
        string message,
        string sessionId,
        string developerEmail,
        CancellationToken ct)
    {
        try
        {
            await channel.NotifyViolationAsync(ruleId, severity, message, sessionId, developerEmail, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Notification channel {Channel} failed for rule {RuleId} in session {SessionId}",
                channelName, ruleId, sessionId);
        }
    }
}
