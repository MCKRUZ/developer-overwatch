namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;

/// <summary>Adds up usage slots into the summary's totals and split rows.</summary>
public static class UsageAggregation
{
    public const string MainThreadLabel = "main thread";
    public const string UnknownLabel = "unknown";
    private const int SessionLabelLength = 8;

    public static UsageTotals Totals(IEnumerable<UsageSlotRow> slots)
    {
        long input = 0, output = 0, cacheWrite = 0, cacheRead = 0;
        var cost = 0m;
        var turns = 0;
        foreach (var slot in slots)
        {
            input += slot.InputTokens;
            output += slot.OutputTokens;
            cacheWrite += slot.CacheCreationInputTokens;
            cacheRead += slot.CacheReadInputTokens;
            cost += slot.EstimatedCostUsd;
            turns += slot.TurnCount;
        }

        return new UsageTotals(input, output, cacheWrite, cacheRead, input + output + cacheWrite + cacheRead,
            cost, CacheHitRate.Of(input, cacheRead, cacheWrite), turns);
    }

    /// <summary>One row per <paramref name="dimension"/> key, sorted by cost (highest first), then by label so equal
    /// costs keep a stable order.</summary>
    public static IReadOnlyList<UsageSplitRow> Split(
        IEnumerable<UsageSlotRow> slots, UsageDimension dimension, decimal periodCost) =>
        slots
            .GroupBy(s => s.KeyOf(dimension))
            .Select(g =>
            {
                var totals = Totals(g);
                var share = periodCost == 0m ? 0d : (double)(totals.EstimatedCostUsd / periodCost);
                return new UsageSplitRow(g.Key, Label(dimension, g.Key), share,
                    g.Min(s => s.FirstAtUtc), g.Max(s => s.LastAtUtc), totals);
            })
            .OrderByDescending(r => r.EstimatedCostUsd)
            .ThenBy(r => r.Label, StringComparer.Ordinal)
            .ToList();

    public static string Label(UsageDimension dimension, string? key) => (dimension, key) switch
    {
        (UsageDimension.Subagent, null) => MainThreadLabel,
        (_, null) => UnknownLabel,
        (UsageDimension.Session, _) => key.Length > SessionLabelLength ? key[..SessionLabelLength] : key,
        _ => key,
    };
}
