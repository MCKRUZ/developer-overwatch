namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class ArchitectAnnotationRepository : IArchitectAnnotationRepository
{
    private readonly OverwatchDbContext _db;

    public ArchitectAnnotationRepository(OverwatchDbContext db) => _db = db;

    public async Task<ArchitectAnnotation?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<ArchitectAnnotation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ArchitectAnnotation annotation, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations.AddAsync(annotation, ct);
}
