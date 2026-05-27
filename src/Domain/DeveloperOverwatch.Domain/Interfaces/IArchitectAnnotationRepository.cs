namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IArchitectAnnotationRepository
{
    Task<ArchitectAnnotation?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ArchitectAnnotation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task AddAsync(ArchitectAnnotation annotation, CancellationToken ct = default);
}
