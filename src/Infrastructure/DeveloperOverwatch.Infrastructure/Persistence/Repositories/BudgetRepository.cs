namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class BudgetRepository : IBudgetRepository
{
    private readonly OverwatchDbContext _db;

    public BudgetRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default)
    {
        var budgets = await _db.Budgets.ToListAsync(ct);
        // Sorted here: the enums are stored as names, so SQL would order "Developer" before "Team".
        return budgets
            .OrderBy(b => b.Scope)
            .ThenBy(b => b.DeveloperEmail, StringComparer.Ordinal)
            .ThenBy(b => b.Period)
            .ToList();
    }

    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Budgets.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<Budget?> FindAsync(string subjectKey, BudgetPeriod period, CancellationToken ct = default) =>
        await _db.Budgets.FirstOrDefaultAsync(b => b.SubjectKey == subjectKey && b.Period == period, ct);

    public async Task AddAsync(Budget budget, CancellationToken ct = default) =>
        await _db.Budgets.AddAsync(budget, ct);

    public void Remove(Budget budget) => _db.Budgets.Remove(budget);
}
