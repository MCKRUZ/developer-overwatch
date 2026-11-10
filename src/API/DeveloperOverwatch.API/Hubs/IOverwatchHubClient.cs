namespace DeveloperOverwatch.API.Hubs;

public interface IOverwatchHubClient
{
    Task SessionStarted(string sessionId, string developerEmail, string? repoPath);
    Task SessionEnded(string sessionId);
    Task EventsIngested(string sessionId, int turnCount, int toolCount, int fileCount);
    Task ViolationCreated(ViolationDto violation);
    Task ViolationResolved(string violationId);

    /// <summary>New turns, an activity-state change or a new tool failure was stored, so the usage page's
    /// totals or running list changed; the dashboard refetches what it shows.</summary>
    Task UsageUpdated(UsageUpdatedNotification notification);
}

/// <summary>Sent as one object: <c>{ sessionId, at }</c>, with <c>at</c> in UTC. It carries no developer,
/// because every connected dashboard receives it.</summary>
public sealed record UsageUpdatedNotification(string SessionId, DateTime At);

public sealed record ViolationDto(
    string Id,
    string SessionId,
    string RuleId,
    string Severity,
    string Message);
