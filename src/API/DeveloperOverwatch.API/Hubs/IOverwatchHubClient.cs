namespace DeveloperOverwatch.API.Hubs;

public interface IOverwatchHubClient
{
    Task SessionStarted(string sessionId, string developerEmail, string? repoPath);
    Task SessionEnded(string sessionId);
    Task EventsIngested(string sessionId, int turnCount, int toolCount, int fileCount);
    Task ViolationCreated(ViolationDto violation);
    Task ViolationResolved(string violationId);

    /// <summary>New turns were stored, so usage totals changed; the dashboard refetches what it shows.</summary>
    Task UsageUpdated(UsageUpdatedNotification notification);
}

/// <summary>Sent as one object: <c>{ sessionId, developerEmail, at }</c>, with <c>at</c> in UTC.</summary>
public sealed record UsageUpdatedNotification(string SessionId, string DeveloperEmail, DateTime At);

public sealed record ViolationDto(
    string Id,
    string SessionId,
    string RuleId,
    string Severity,
    string Message);
