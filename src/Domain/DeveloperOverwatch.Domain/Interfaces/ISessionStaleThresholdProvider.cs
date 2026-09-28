namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>Configured idle threshold past which an "Active" session is displayed as "Stale" —
/// it never got a SessionEnd (crash, laptop sleep, dropped request), not because it's still
/// genuinely running. A resume reactivates it with no special handling needed.</summary>
public interface ISessionStaleThresholdProvider
{
    int StaleAfterMinutes { get; }
}
