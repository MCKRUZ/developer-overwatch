using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>Token counts by kind and their estimated cost, summed over some usage rows.</summary>
public sealed record TokenTotals(long Input, long Output, long CacheWrite, long CacheRead, decimal Usd)
{
    public static readonly TokenTotals Zero = new(0, 0, 0, 0, 0m);

    public long Total => Input + Output + CacheWrite + CacheRead;

    public TokenTotals Add(UsageRecord row, decimal usd) => new(
        Input + row.InputTokens,
        Output + row.OutputTokens,
        CacheWrite + row.CacheCreationTokens,
        CacheRead + row.CacheReadTokens,
        Usd + usd);
}
