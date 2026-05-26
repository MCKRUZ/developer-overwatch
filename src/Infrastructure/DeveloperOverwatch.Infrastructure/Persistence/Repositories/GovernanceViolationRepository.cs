namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class GovernanceViolationRepository : IGovernanceViolationRepository
{
    private readonly OverwatchDbContext _db;

    public GovernanceViolationRepository(OverwatchDbContext db) => _db = db;

    public async Task<GovernanceViolation?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.GovernanceViolations.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        await _db.GovernanceViolations
            .Where(v => v.SessionId == sessionId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, ViolationSeverity? severity, CancellationToken ct = default)
    {
        var query = _db.GovernanceViolations.Where(v => v.SessionId == sessionId);
        if (severity.HasValue)
            query = query.Where(v => v.Severity == severity.Value);
        return await query.OrderByDescending(v => v.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GovernanceViolation>> GetRecentAsync(TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;
        return await _db.GovernanceViolations
            .Where(v => v.CreatedAt >= cutoff)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<GovernanceViolation> violations, CancellationToken ct = default) =>
        await _db.GovernanceViolations.AddRangeAsync(violations, ct);

    public Task UpdateAsync(GovernanceViolation violation, CancellationToken ct = default)
    {
        _db.GovernanceViolations.Update(violation);
        return Task.CompletedTask;
    }
}
