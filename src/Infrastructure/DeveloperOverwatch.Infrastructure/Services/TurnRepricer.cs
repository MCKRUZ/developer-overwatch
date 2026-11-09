namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Recomputes stored turn costs that were priced by a different price table than the current one,
/// so every dollar figure matches the "prices as of" date the dashboard shows. Runs at startup.
/// </summary>
public sealed class TurnRepricer
{
    // A batch sends up to 500 turn ids plus 500 session ids as query parameters, well under
    // SQL Server's 2,100-parameter limit.
    public const int BatchSize = 500;

    private readonly OverwatchDbContext _db;
    private readonly ITokenPricingCalculator _pricing;
    private readonly ILogger<TurnRepricer> _logger;

    public TurnRepricer(OverwatchDbContext db, ITokenPricingCalculator pricing, ILogger<TurnRepricer> logger)
    {
        _db = db;
        _pricing = pricing;
        _logger = logger;
    }

    public async Task<TurnRepriceResult> RepriceAsync(CancellationToken ct = default)
    {
        var pricesAsOf = _pricing.PricesAsOf;
        var turnCount = 0;
        var sessionIds = new HashSet<string>();

        while (true)
        {
            var batch = await RepriceNextBatchAsync(pricesAsOf, ct);
            if (batch is null)
                break;

            turnCount += batch.TurnCount;
            sessionIds.UnionWith(batch.SessionIds);
        }

        if (turnCount > 0)
        {
            _logger.LogInformation(
                "Repriced {TurnCount} conversation turns across {SessionCount} sessions to prices as of {PricesAsOf}",
                turnCount, sessionIds.Count, pricesAsOf);
        }

        return new TurnRepriceResult(turnCount, sessionIds.Count);
    }

    /// <summary>
    /// Reprices one batch of stale turns and the totals of the sessions they belong to in a single
    /// SaveChanges, so a crash can never leave a repriced turn behind a stale session total.
    /// Returns null when no stale turns remain.
    /// </summary>
    private async Task<BatchResult?> RepriceNextBatchAsync(DateOnly pricesAsOf, CancellationToken ct)
    {
        // Repriced turns stop matching the filter, so each query picks up where the last one ended.
        var batch = await _db.ConversationTurns
            .Where(t => t.InputTokens != null || t.OutputTokens != null
                || t.CacheCreationInputTokens != null || t.CacheReadInputTokens != null)
            .Where(t => t.PricesAsOf == null || t.PricesAsOf != pricesAsOf)
            .OrderBy(t => t.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (batch.Count == 0)
            return null;

        foreach (var turn in batch)
        {
            turn.EstimatedCostUsd = _pricing.CalculateCost(
                turn.Model, turn.InputTokens, turn.OutputTokens,
                turn.CacheCreationInputTokens, turn.CacheReadInputTokens);
            turn.PricesAsOf = pricesAsOf;
        }

        var sessionIds = batch.Select(t => t.SessionId).Distinct().ToList();
        var totals = await SessionTotalsAsync(batch, sessionIds, ct);
        var sessions = await _db.Sessions.Where(s => sessionIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var session in sessions)
            session.EstimatedCostUsd = totals.GetValueOrDefault(session.Id);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return new BatchResult(batch.Count, sessionIds);
    }

    /// <summary>Per-session sum of turn costs: stored costs for other turns, new costs for this batch.</summary>
    private async Task<Dictionary<string, decimal>> SessionTotalsAsync(
        IReadOnlyList<Domain.Entities.ConversationTurn> batch, List<string> sessionIds, CancellationToken ct)
    {
        var batchIds = batch.Select(t => t.Id).ToList();

        // Summed in memory: SQLite stores decimals as text and cannot aggregate them exactly.
        var otherCosts = await _db.ConversationTurns
            .AsNoTracking()
            .Where(t => sessionIds.Contains(t.SessionId) && !batchIds.Contains(t.Id))
            .Select(t => new { t.SessionId, t.EstimatedCostUsd })
            .ToListAsync(ct);

        return otherCosts
            .Select(t => (t.SessionId, Cost: t.EstimatedCostUsd ?? 0m))
            .Concat(batch.Select(t => (t.SessionId, Cost: t.EstimatedCostUsd ?? 0m)))
            .GroupBy(t => t.SessionId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Cost));
    }

    private sealed record BatchResult(int TurnCount, IReadOnlyList<string> SessionIds);
}
