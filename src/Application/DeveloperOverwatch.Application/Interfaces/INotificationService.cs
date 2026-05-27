namespace DeveloperOverwatch.Application.Interfaces;

public interface INotificationService
{
    Task NotifyViolationAsync(
        string ruleId,
        string severity,
        string message,
        string sessionId,
        string developerEmail,
        CancellationToken ct = default);
}
