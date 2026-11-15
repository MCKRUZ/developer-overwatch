namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class SessionInsightRepository : ISessionInsightRepository
{
    private readonly OverwatchDbContext _db;

    public SessionInsightRepository(OverwatchDbContext db) => _db = db;

    public Task<bool> SessionExistsAsync(string sessionId, CancellationToken ct = default) =>
        _db.Sessions.AnyAsync(s => s.Id == sessionId, ct);

    public Task<SessionContextHeader?> GetContextHeaderAsync(string sessionId, CancellationToken ct = default) =>
        _db.Sessions
            .Where(s => s.Id == sessionId)
            .Select(s => new SessionContextHeader(
                s.PeakContextTokens, s.PeakContextModel, s.ToolErrorCount, s.LastToolErrorName, s.LastToolErrorAt))
            .FirstOrDefaultAsync(ct);

    public Task<ContextReading?> GetLatestMainThreadContextAsync(string sessionId, CancellationToken ct = default) =>
        _db.ConversationTurns
            .Where(t => t.SessionId == sessionId
                && t.Role != SessionTurnCostRow.SubagentRole
                && t.ContextTokens > 0)
            .OrderByDescending(t => t.Timestamp)
            .ThenByDescending(t => t.TurnIndex)
            .Select(t => new ContextReading(t.Model, t.ContextTokens!.Value))
            .FirstOrDefaultAsync(ct);

    // Split, because one query would join the three owned collections into categories x tools x compactions rows.
    public Task<SessionContextSnapshot?> GetContextSnapshotAsync(string sessionId, CancellationToken ct = default) =>
        _db.SessionContextSnapshots
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    public async Task<IReadOnlyList<SessionTurnCostRow>> GetTurnCostRowsAsync(
        string sessionId, CancellationToken ct = default) =>
        await _db.ConversationTurns
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.TurnIndex)
            .Select(t => new SessionTurnCostRow(
                t.TurnIndex,
                t.Timestamp,
                t.Role,
                t.TurnKind,
                t.SubagentName,
                t.Role == SessionTurnCostRow.UserRole && t.Content != null
                    ? t.Content.Substring(0, SessionTurnCostRow.PromptTextLength)
                    : null,
                t.InputTokens,
                t.OutputTokens,
                t.CacheCreationInputTokens,
                t.CacheReadInputTokens,
                t.EstimatedCostUsd))
            .ToListAsync(ct);
}
