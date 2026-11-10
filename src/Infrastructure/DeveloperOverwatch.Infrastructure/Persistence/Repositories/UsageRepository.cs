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

    // One GROUP BY in the database. Sums cast each column to long first: SQL Server sums an int column as int,
    // and a month of a team's cache reads passes int.MaxValue.
    public async Task<IReadOnlyList<UsageSlotRow>> GetSlotsAsync(
        DateTime fromUtc, DateTime toUtc, UsageFilter filter, UsageDimension dimension, CancellationToken ct = default)
    {
        var turns = Matching(filter).Where(t => t.Timestamp >= fromUtc && t.Timestamp < toUtc);
        var groups = await KeyedBy(turns, dimension)
            .GroupBy(k => new { k.Key, k.Turn.Timestamp.Date, k.Turn.Timestamp.Hour, Quarter = k.Turn.Timestamp.Minute / 15 })
            .Select(g => new
            {
                DimensionKey = g.Key.Key,
                g.Key.Date,
                g.Key.Hour,
                g.Key.Quarter,
                Input = g.Sum(k => (long)(k.Turn.InputTokens ?? 0)),
                Output = g.Sum(k => (long)(k.Turn.OutputTokens ?? 0)),
                CacheWrite = g.Sum(k => (long)(k.Turn.CacheCreationInputTokens ?? 0)),
                CacheRead = g.Sum(k => (long)(k.Turn.CacheReadInputTokens ?? 0)),
                Cost = g.Sum(k => k.Turn.EstimatedCostUsd ?? 0m),
                Count = g.Count(),
                FirstAt = g.Min(k => k.Turn.Timestamp),
                LastAt = g.Max(k => k.Turn.Timestamp),
            })
            .ToListAsync(ct);

        return groups
            .Select(g => new UsageSlotRow(
                AsUtc(g.Date).AddHours(g.Hour).AddMinutes(15 * g.Quarter), g.DimensionKey,
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

    private IQueryable<ConversationTurn> Matching(UsageFilter filter)
    {
        var turns = _db.ConversationTurns.AsQueryable();
        if (filter.DeveloperEmail is { } email)
            turns = turns.Where(t => t.Session!.DeveloperEmail == email);
        if (filter.Project is { } project)
            turns = turns.Where(t => (t.Session!.ProjectKey ?? t.Session.RepoPath) == project);
        return turns;
    }

    private static IQueryable<KeyedTurn> KeyedBy(IQueryable<ConversationTurn> turns, UsageDimension dimension) =>
        dimension switch
        {
            UsageDimension.Model => turns.Select(t => new KeyedTurn { Key = t.Model, Turn = t }),
            UsageDimension.Project => turns.Select(t => new KeyedTurn { Key = t.Session!.ProjectKey ?? t.Session.RepoPath, Turn = t }),
            UsageDimension.Session => turns.Select(t => new KeyedTurn { Key = t.SessionId, Turn = t }),
            UsageDimension.Subagent => turns.Select(t => new KeyedTurn { Key = t.SubagentName, Turn = t }),
            UsageDimension.Branch => turns.Select(t => new KeyedTurn { Key = t.GitBranch, Turn = t }),
            UsageDimension.Developer => turns.Select(t => new KeyedTurn { Key = t.Session!.DeveloperEmail, Turn = t }),
            UsageDimension.Machine => turns.Select(t => new KeyedTurn { Key = t.Session!.MachineName, Turn = t }),
            UsageDimension.Agent => turns.Select(t => new KeyedTurn { Key = t.Session!.Agent, Turn = t }),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
        };

    // Stored times are UTC, but SQLite hands them back unmarked.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private IQueryable<Session> RunningSessions(DateTime activeSince) =>
        _db.Sessions.Where(s => s.Status == SessionStatus.Active && s.LastActivityAt >= activeSince);

    // A member-init class, not a positional record: EF Core can translate member access on it after GroupBy.
    private sealed class KeyedTurn
    {
        public string? Key { get; init; }

        public ConversationTurn Turn { get; init; } = null!;
    }
}
