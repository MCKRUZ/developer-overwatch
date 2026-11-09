namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

/// <summary>What a session end did. <see cref="Outcome"/> is what the Agent is told.
/// <see cref="ClosedSession"/> is true only when this end moved an active session to completed, so
/// work that belongs to a session's close (governance) runs once, never on a redelivery.</summary>
public sealed record IngestSessionEndResult(IngestOutcome Outcome, bool ClosedSession);
