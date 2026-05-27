namespace DeveloperOverwatch.Infrastructure.Services;

using System.Net;
using System.Net.Mail;
using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class EmailNotificationService : INotificationService
{
    private readonly IOptionsMonitor<EmailConfig> _config;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IOptionsMonitor<EmailConfig> config,
        ILogger<EmailNotificationService> logger)
    {
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

        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.SmtpHost))
        {
            _logger.LogDebug("Email notifications disabled or SMTP host not configured");
            return;
        }

        if (cfg.ToAddresses.Length == 0)
        {
            _logger.LogDebug("No email recipients configured");
            return;
        }

        if (!MeetsSeverityThreshold(severity, cfg.MinimumSeverity))
            return;

        var truncatedSession = sessionId.Length > 8 ? sessionId[..8] : sessionId;
        var severityColor = severity switch
        {
            "Critical" => "#dc3545",
            "Error" => "#fd7e14",
            _ => "#808080"
        };

        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; padding: 20px;">
                <h2 style="color: {severityColor};">Governance Violation: {WebUtility.HtmlEncode(ruleId)}</h2>
                <table style="border-collapse: collapse; width: 100%; max-width: 600px;">
                    <tr>
                        <td style="padding: 8px; font-weight: bold; border-bottom: 1px solid #ddd;">Severity</td>
                        <td style="padding: 8px; border-bottom: 1px solid #ddd; color: {severityColor};">{WebUtility.HtmlEncode(severity)}</td>
                    </tr>
                    <tr>
                        <td style="padding: 8px; font-weight: bold; border-bottom: 1px solid #ddd;">Rule ID</td>
                        <td style="padding: 8px; border-bottom: 1px solid #ddd;">{WebUtility.HtmlEncode(ruleId)}</td>
                    </tr>
                    <tr>
                        <td style="padding: 8px; font-weight: bold; border-bottom: 1px solid #ddd;">Developer</td>
                        <td style="padding: 8px; border-bottom: 1px solid #ddd;">{WebUtility.HtmlEncode(developerEmail)}</td>
                    </tr>
                    <tr>
                        <td style="padding: 8px; font-weight: bold; border-bottom: 1px solid #ddd;">Session</td>
                        <td style="padding: 8px; border-bottom: 1px solid #ddd;">{WebUtility.HtmlEncode(truncatedSession)}</td>
                    </tr>
                    <tr>
                        <td style="padding: 8px; font-weight: bold; border-bottom: 1px solid #ddd;">Message</td>
                        <td style="padding: 8px; border-bottom: 1px solid #ddd;">{WebUtility.HtmlEncode(message)}</td>
                    </tr>
                </table>
            </body>
            </html>
            """;

        await SendEmailAsync(cfg, ruleId, severity, htmlBody, ct);
    }

    private async Task SendEmailAsync(EmailConfig cfg, string ruleId, string severity, string htmlBody, CancellationToken ct)
    {
#pragma warning disable SYSLIB0014 // SmtpClient is obsolete but acceptable for simple use cases
        using var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort)
        {
            EnableSsl = cfg.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
#pragma warning restore SYSLIB0014

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(cfg.FromAddress),
            Subject = $"[{severity}] Governance Violation: {ruleId}",
            Body = htmlBody,
            IsBodyHtml = true
        };

        foreach (var to in cfg.ToAddresses)
            mailMessage.To.Add(to);

        await client.SendMailAsync(mailMessage, ct);
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
