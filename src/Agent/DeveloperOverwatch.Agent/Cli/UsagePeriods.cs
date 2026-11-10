using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>
/// The report's period starts as UTC instants: local midnight today, local Monday midnight, and local
/// midnight on the first of the month, in the given zone.
/// </summary>
public sealed record UsagePeriods(DateTime NowUtc, DateTime TodayStartUtc, DateTime WeekStartUtc, DateTime MonthStartUtc)
{
    /// <summary>UsageWindow needs history back to the last idle gap of a full window; a day covers that in practice.</summary>
    private static readonly TimeSpan WindowLookback = TimeSpan.FromHours(24);

    public DateTime WindowActivityStartUtc => NowUtc - WindowLookback;

    /// <summary>The earliest instant any part of the report reads; a week can start in the previous month.</summary>
    public DateTime QueryStartUtc => new[] { WeekStartUtc, MonthStartUtc, WindowActivityStartUtc }.Min();

    public static UsagePeriods For(DateTime nowUtc, TimeZoneInfo zone) => new(
        nowUtc,
        LocalCalendar.TodayStartUtc(nowUtc, zone),
        LocalCalendar.WeekStartUtc(nowUtc, zone),
        LocalCalendar.MonthStartUtc(nowUtc, zone));
}
