namespace DeveloperOverwatch.API.Hubs;

public interface IOverwatchHubClient
{
    Task SessionStarted(string sessionId, string developerEmail, string? repoPath);
    Task SessionEnded(string sessionId);
    Task EventsIngested(string sessionId, int turnCount, int toolCount, int fileCount);
    Task ViolationCreated(ViolationDto violation);
    Task ViolationResolved(string violationId);
}

public sealed record ViolationDto(
    string Id,
    string SessionId,
    string RuleId,
    string Severity,
    string Message);
