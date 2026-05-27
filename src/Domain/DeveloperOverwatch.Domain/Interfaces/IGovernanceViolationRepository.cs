namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;

public interface IGovernanceViolationRepository
{
    Task<GovernanceViolation?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, ViolationSeverity? severity, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetRecentAsync(TimeSpan window, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetAllAsync(CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<GovernanceViolation> violations, CancellationToken ct = default);
    Task UpdateAsync(GovernanceViolation violation, CancellationToken ct = default);
}
