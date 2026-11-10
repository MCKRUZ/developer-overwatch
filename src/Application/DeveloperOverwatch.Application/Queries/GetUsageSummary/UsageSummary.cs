namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

// The response shapes of GET /api/usage/summary. The dashboard reads exactly these field names (camelCase), so
// a bucket and a split row carry the totals' fields flat, which is why they derive from UsageTotals.

/// <summary>Token and cost sums for a set of turns.</summary>
/// <param name="CacheHitRate">Cache reads over all input (0..1); null, and so omitted, when there was no input.</param>
public record UsageTotals(
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    long TotalTokens,
    decimal EstimatedCostUsd,
    double? CacheHitRate,
    int TurnCount);

/// <summary>One day, ISO week or calendar month of the period.</summary>
public sealed record UsageBucket : UsageTotals
{
    public UsageBucket(DateOnly start, UsageTotals totals) : base(totals) => Start = start;

    /// <summary>Local date the bucket starts: the day, the week's Monday, or the month's 1st.</summary>
    public DateOnly Start { get; }
}

/// <summary>One key's share of the period.</summary>
public sealed record UsageSplitRow : UsageTotals
{
    public UsageSplitRow(string? key, string label, double costShare, DateTime firstAt, DateTime lastAt, UsageTotals totals)
        : base(totals) => (Key, Label, CostShare, FirstAt, LastAt) = (key, label, costShare, firstAt, lastAt);

    /// <summary>The dimension's value; null, and so omitted, for the main thread or an unknown value.</summary>
    public string? Key { get; }

    public string Label { get; }

    /// <summary>This row's share of the period's cost (0..1); 0 when the period cost nothing.</summary>
    public double CostShare { get; }

    /// <summary>Earliest turn, UTC.</summary>
    public DateTime FirstAt { get; }

    /// <summary>Latest turn, UTC.</summary>
    public DateTime LastAt { get; }
}

/// <summary>The period split each way; every list is sorted by estimated cost, highest first.</summary>
public sealed record UsageSplits(
    IReadOnlyList<UsageSplitRow> Model,
    IReadOnlyList<UsageSplitRow> Project,
    IReadOnlyList<UsageSplitRow> Session,
    IReadOnlyList<UsageSplitRow> Subagent,
    IReadOnlyList<UsageSplitRow> Branch,
    IReadOnlyList<UsageSplitRow> Developer,
    IReadOnlyList<UsageSplitRow> Machine,
    IReadOnlyList<UsageSplitRow> Agent);

/// <summary>Consumption per minute over the last <paramref name="LookbackMinutes"/> before now, all four token
/// kinds, with the developer and project filters applied but not the period.</summary>
public sealed record UsageBurn(double TokensPerMinute, decimal CostPerMinute, int LookbackMinutes);

/// <summary>The usage page's whole read for one period.</summary>
/// <param name="PricesAsOf">Date of the price table every estimated cost was computed from.</param>
/// <param name="Buckets">Every bucket in the period, zero-filled, ascending.</param>
public sealed record UsageSummary(
    DateOnly From,
    DateOnly To,
    string Granularity,
    string TimeZone,
    DateOnly PricesAsOf,
    UsageTotals Totals,
    IReadOnlyList<UsageBucket> Buckets,
    UsageSplits Splits,
    UsageBurn Burn);
