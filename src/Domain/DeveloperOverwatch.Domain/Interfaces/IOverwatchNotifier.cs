namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IOverwatchNotifier
{
    Task ViolationCreatedAsync(GovernanceViolation violation, CancellationToken ct = default);
}
