namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;

public interface IBudgetRepository
{
    /// <summary>Every budget: the team first, then developers by email (ordinal), daily before monthly.</summary>
    Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default);

    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The budget for <paramref name="subjectKey"/> (see <see cref="Budget.SubjectKeyFor"/>) and
    /// <paramref name="period"/>, of which there is at most one.</summary>
    Task<Budget?> FindAsync(string subjectKey, BudgetPeriod period, CancellationToken ct = default);

    Task AddAsync(Budget budget, CancellationToken ct = default);

    void Remove(Budget budget);
}
