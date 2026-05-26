namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class SessionRepository : ISessionRepository
{
    private readonly OverwatchDbContext _db;

    public SessionRepository(OverwatchDbContext db) => _db = db;

    public async Task<Session?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.Sessions
            .Include(s => s.ConversationTurns.OrderBy(t => t.TurnIndex))
            .Include(s => s.ToolExecutions.OrderBy(t => t.Timestamp))
            .Include(s => s.FileChanges)
            .Include(s => s.GovernanceViolations)
            .Include(s => s.ArchitectAnnotations)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Session>> GetAllAsync(
        string? developerEmail = null, string? repo = null, CancellationToken ct = default)
    {
        var query = _db.Sessions.AsQueryable();
        if (developerEmail is not null)
            query = query.Where(s => s.DeveloperEmail == developerEmail);
        if (repo is not null)
            query = query.Where(s => s.RepoPath == repo);
        return await query.OrderByDescending(s => s.StartedAt).ToListAsync(ct);
    }

    public async Task AddAsync(Session session, CancellationToken ct = default) =>
        await _db.Sessions.AddAsync(session, ct);

    public Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        _db.Sessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddAsync(turn, ct);

    public async Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddAsync(toolExecution, ct);

    public async Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default) =>
        await _db.FileChanges.AddAsync(fileChange, ct);

    public async Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddRangeAsync(turns, ct);

    public async Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddRangeAsync(toolExecutions, ct);

    public async Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default) =>
        await _db.FileChanges.AddRangeAsync(fileChanges, ct);
}
