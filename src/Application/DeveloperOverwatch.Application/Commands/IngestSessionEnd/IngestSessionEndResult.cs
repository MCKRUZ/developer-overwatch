namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

/// <summary>What a session end did. <see cref="Outcome"/> is what the Agent is told.
/// <see cref="ClosedSession"/> is true only when this end moved an active session to completed, so
/// work that belongs to a session's close (governance) never runs on a redelivery or a no-op end. A
/// session resumed and closed again has a second close, and that end is true again.</summary>
public sealed record IngestSessionEndResult(IngestOutcome Outcome, bool ClosedSession);
