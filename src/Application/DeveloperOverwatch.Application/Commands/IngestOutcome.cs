namespace DeveloperOverwatch.Application.Commands;

/// <summary>What a session start or end did. The Agent marks the event done on either outcome.
/// <see cref="Duplicate"/> means its event id was already processed, so nothing changed.</summary>
public enum IngestOutcome
{
    Accepted,
    Duplicate
}
