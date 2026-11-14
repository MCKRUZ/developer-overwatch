namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using Microsoft.EntityFrameworkCore;

public sealed class UsageRepository : IUsageRepository
{
    private readonly OverwatchDbContext _db;

    public UsageRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<RunningSessionRow>> GetRunningSessionsAsync(
        DateTime activeSince, UsageFilter filter, CancellationToken ct = default) =>
        await RunningSessions(activeSince, filter)
            .Select(s => new RunningSessionRow(
                s.Id, s.DeveloperEmail, s.DeveloperName, s.RepoPath, s.ProjectKey, s.MachineName, s.Agent,
                s.ActivityState, AsUtc(s.ActivityStateAt), AsUtc(s.LastActivityAt),
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
        DateTime activeSince, DateTime turnsAfter, UsageFilter filter, CancellationToken ct = default) =>
        await Matching(filter)
            .Where(t => t.Timestamp > turnsAfter
                && t.Session!.Status == SessionStatus.Active
                && t.Session.LastActivityAt >= activeSince)
            .Select(t => new SessionTurnUsageRow(
                t.SessionId, t.Timestamp, t.InputTokens, t.OutputTokens,
                t.CacheCreationInputTokens, t.CacheReadInputTokens, t.EstimatedCostUsd))
            .ToListAsync(ct);

    // One GROUP BY in the database. The session's attributes depend only on SessionId, so grouping on them adds
    // no rows; it just carries them through the join the filter already makes. Sums cast each column to long
    // first: SQL Server sums an int column as int, and a month of a team's cache reads passes int.MaxValue.
    public async Task<IReadOnlyList<UsageSlotRow>> GetSlotsAsync(
        DateTime fromUtc, DateTime toUtc, UsageFilter filter, CancellationToken ct = default)
    {
        var groups = await Matching(filter)
            .Where(t => t.Timestamp >= fromUtc && t.Timestamp < toUtc)
            .GroupBy(t => new
            {
                t.Timestamp.Date,
                t.Timestamp.Hour,
                Quarter = t.Timestamp.Minute / 15,
                t.SessionId,
                t.Model,
                t.SubagentName,
                t.GitBranch,
                Project = t.Session!.ProjectKey ?? t.Session.RepoPath,
                t.Session.DeveloperEmail,
                t.Session.MachineName,
                t.Session.Agent,
            })
            .Select(g => new
            {
                g.Key,
                Input = g.Sum(t => (long)(t.InputTokens ?? 0)),
                Output = g.Sum(t => (long)(t.OutputTokens ?? 0)),
                CacheWrite = g.Sum(t => (long)(t.CacheCreationInputTokens ?? 0)),
                CacheRead = g.Sum(t => (long)(t.CacheReadInputTokens ?? 0)),
                Cost = g.Sum(t => t.EstimatedCostUsd ?? 0m),
                Count = g.Count(),
                FirstAt = g.Min(t => t.Timestamp),
                LastAt = g.Max(t => t.Timestamp),
            })
            .ToListAsync(ct);

        return groups
            .Select(g => new UsageSlotRow(
                AsUtc(g.Key.Date).AddHours(g.Key.Hour).AddMinutes(15 * g.Key.Quarter),
                g.Key.SessionId, g.Key.Model, g.Key.SubagentName, g.Key.GitBranch,
                g.Key.Project, g.Key.DeveloperEmail, g.Key.MachineName, g.Key.Agent,
                g.Input, g.Output, g.CacheWrite, g.CacheRead, g.Cost, g.Count, AsUtc(g.FirstAt), AsUtc(g.LastAt)))
            .ToList();
    }

    public async Task<IReadOnlyList<UsagePoint>> GetTurnPointsAsync(
        DateTime afterUtc, DateTime untilUtc, UsageFilter filter, CancellationToken ct = default)
    {
        var points = await Matching(filter)
            .Where(t => t.Timestamp > afterUtc && t.Timestamp <= untilUtc)
            .Select(t => new
            {
                t.Timestamp,
                Tokens = (long)(t.InputTokens ?? 0) + (t.OutputTokens ?? 0)
                    + (t.CacheCreationInputTokens ?? 0) + (t.CacheReadInputTokens ?? 0),
                Cost = t.EstimatedCostUsd ?? 0m,
            })
            .ToListAsync(ct);

        return points.Select(p => new UsagePoint(AsUtc(p.Timestamp), p.Tokens, p.Cost)).ToList();
    }

    public async Task<IReadOnlyList<DeveloperTurnPoint>> GetDeveloperTurnPointsAsync(
        DateTime afterUtc, DateTime untilUtc, CancellationToken ct = default)
    {
        var points = await _db.ConversationTurns
            .Where(t => t.Timestamp > afterUtc && t.Timestamp <= untilUtc)
            .Select(t => new
            {
                t.Session!.DeveloperEmail,
                t.Timestamp,
                Tokens = (long)(t.InputTokens ?? 0) + (t.OutputTokens ?? 0)
                    + (t.CacheCreationInputTokens ?? 0) + (t.CacheReadInputTokens ?? 0),
                Cost = t.EstimatedCostUsd ?? 0m,
            })
            .ToListAsync(ct);

        return points
            .Select(p => new DeveloperTurnPoint(p.DeveloperEmail, AsUtc(p.Timestamp), p.Tokens, p.Cost))
            .ToList();
    }

    private IQueryable<ConversationTurn> Matching(UsageFilter filter)
    {
        var turns = _db.ConversationTurns.AsQueryable();
        if (filter.DeveloperEmail is { } email)
            turns = turns.Where(t => t.Session!.DeveloperEmail == email);
        if (filter.Project is { } project)
            turns = turns.Where(t => (t.Session!.ProjectKey ?? t.Session.RepoPath) == project);
        return turns;
    }

    // Stored times are UTC, but SQLite hands them back unmarked.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value is { } at ? AsUtc(at) : null;

    private IQueryable<Session> RunningSessions(DateTime activeSince, UsageFilter filter)
    {
        var sessions = _db.Sessions.Where(s => s.Status == SessionStatus.Active && s.LastActivityAt >= activeSince);
        if (filter.DeveloperEmail is { } email)
            sessions = sessions.Where(s => s.DeveloperEmail == email);
        if (filter.Project is { } project)
            sessions = sessions.Where(s => (s.ProjectKey ?? s.RepoPath) == project);
        return sessions;
    }
}
