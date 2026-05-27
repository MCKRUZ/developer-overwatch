namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class CommitSessionLinkRepository : ICommitSessionLinkRepository
{
    private readonly OverwatchDbContext _db;

    public CommitSessionLinkRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<CommitSessionLink>> GetByCommitShaAsync(
        string commitSha, CancellationToken ct = default) =>
        await _db.CommitSessionLinks
            .Include(l => l.Session)
            .Where(l => l.CommitSha == commitSha)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CommitSessionLink>> GetBySessionIdAsync(
        string sessionId, CancellationToken ct = default) =>
        await _db.CommitSessionLinks
            .Where(l => l.SessionId == sessionId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CommitSessionLink>> GetAllAsync(CancellationToken ct = default) =>
        await _db.CommitSessionLinks.ToListAsync(ct);

    public async Task AddAsync(CommitSessionLink link, CancellationToken ct = default) =>
        await _db.CommitSessionLinks.AddAsync(link, ct);

    public async Task AddRangeAsync(IEnumerable<CommitSessionLink> links, CancellationToken ct = default) =>
        await _db.CommitSessionLinks.AddRangeAsync(links, ct);
}
