namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Enums;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Saves like <see cref="SaveChangesAsync"/>, but reports a unique-index violation as
    /// <see cref="SaveOutcome.Duplicate"/> instead of throwing, and stops tracking the refused inserts. For creates
    /// whose "already exists?" check can race another request.</summary>
    Task<SaveOutcome> SaveChangesDetectingDuplicateAsync(CancellationToken ct = default);
}
