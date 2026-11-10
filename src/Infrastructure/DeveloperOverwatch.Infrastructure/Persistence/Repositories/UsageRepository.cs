namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class UsageRepository : IUsageRepository
{
    private readonly OverwatchDbContext _db;

    public UsageRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<RunningSessionRow>> GetRunningSessionsAsync(
        DateTime activeSince, CancellationToken ct = default) =>
        await RunningSessions(activeSince)
            .Select(s => new RunningSessionRow(
                s.Id, s.DeveloperEmail, s.DeveloperName, s.RepoPath, s.ProjectKey, s.MachineName, s.Agent,
                s.ActivityState, s.ActivityStateAt, s.LastActivityAt,
                s.TotalInputTokens, s.TotalOutputTokens, s.TotalCacheCreationInputTokens, s.TotalCacheReadInputTokens,
                s.EstimatedCostUsd,
                s.ConversationTurns
                    .Where(t => t.SubagentName == null && t.ContextTokens != null)
                    .OrderByDescending(t => t.Timestamp)
                    .Select(t => t.ContextTokens)
                    .FirstOrDefault()))
            .ToListAsync(ct);

    // Joined through the session rather than an id list, so the query needs no per-session parameters.
    public async Task<IReadOnlyList<SessionTurnUsageRow>> GetRecentTurnsOfRunningSessionsAsync(
        DateTime activeSince, DateTime turnsAfter, CancellationToken ct = default) =>
        await _db.ConversationTurns
            .Where(t => t.Timestamp > turnsAfter
                && t.Session!.Status == SessionStatus.Active
                && t.Session.LastActivityAt >= activeSince)
            .Select(t => new SessionTurnUsageRow(
                t.SessionId, t.Timestamp, t.InputTokens, t.OutputTokens,
                t.CacheCreationInputTokens, t.CacheReadInputTokens, t.EstimatedCostUsd))
            .ToListAsync(ct);

    private IQueryable<Session> RunningSessions(DateTime activeSince) =>
        _db.Sessions.Where(s => s.Status == SessionStatus.Active && s.LastActivityAt >= activeSince);
}
