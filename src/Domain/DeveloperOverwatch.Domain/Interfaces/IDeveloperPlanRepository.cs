namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IDeveloperPlanRepository
{
    /// <summary>Every plan, by developer email (ordinal).</summary>
    Task<IReadOnlyList<DeveloperPlan>> GetAllAsync(CancellationToken ct = default);

    Task<DeveloperPlan?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The plan of the developer with exactly this (already trimmed) email, of which there is at most one.</summary>
    Task<DeveloperPlan?> FindByEmailAsync(string developerEmail, CancellationToken ct = default);

    Task AddAsync(DeveloperPlan plan, CancellationToken ct = default);

    void Remove(DeveloperPlan plan);
}
