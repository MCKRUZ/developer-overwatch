namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IGovernanceEngine
{
    Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(string sessionId, CancellationToken ct = default);
}
