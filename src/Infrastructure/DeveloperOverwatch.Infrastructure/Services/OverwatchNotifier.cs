namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

public sealed class OverwatchNotifier : IOverwatchNotifier
{
    private readonly INotificationService _notificationService;
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<OverwatchNotifier> _logger;

    public OverwatchNotifier(
        INotificationService notificationService,
        ISessionRepository sessionRepository,
        ILogger<OverwatchNotifier> logger)
    {
        _notificationService = notificationService;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task ViolationCreatedAsync(GovernanceViolation violation, CancellationToken ct = default)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(violation.SessionId, ct);
            var developerEmail = session?.DeveloperEmail ?? "unknown";

            await _notificationService.NotifyViolationAsync(
                violation.RuleId,
                violation.Severity.ToString(),
                violation.Message,
                violation.SessionId,
                developerEmail,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send notification for violation {RuleId} in session {SessionId}",
                violation.RuleId, violation.SessionId);
        }
    }
}
