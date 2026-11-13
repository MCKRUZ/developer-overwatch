namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class DeveloperPlanRepository : IDeveloperPlanRepository
{
    private readonly OverwatchDbContext _db;

    public DeveloperPlanRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeveloperPlan>> GetAllAsync(CancellationToken ct = default)
    {
        var plans = await _db.DeveloperPlans.ToListAsync(ct);
        // Sorted here so the order is ordinal on both providers, not each database's default collation.
        return plans.OrderBy(p => p.DeveloperEmail, StringComparer.Ordinal).ToList();
    }

    public async Task<DeveloperPlan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.DeveloperPlans.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<DeveloperPlan?> FindByEmailAsync(string developerEmail, CancellationToken ct = default) =>
        await _db.DeveloperPlans.FirstOrDefaultAsync(p => p.DeveloperEmail == developerEmail, ct);

    public async Task AddAsync(DeveloperPlan plan, CancellationToken ct = default) =>
        await _db.DeveloperPlans.AddAsync(plan, ct);

    public void Remove(DeveloperPlan plan) => _db.DeveloperPlans.Remove(plan);
}
