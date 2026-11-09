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

    public static UsagePeriods For(DateTime nowUtc, TimeZoneInfo zone)
    {
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
        var daysSinceMonday = ((int)localToday.DayOfWeek + 6) % 7;

        return new UsagePeriods(
            nowUtc,
            ToUtc(localToday, zone),
            ToUtc(localToday.AddDays(-daysSinceMonday), zone),
            ToUtc(new DateTime(localToday.Year, localToday.Month, 1), zone));
    }

    /// <remarks>In a few zones a DST change skips local midnight; the period then starts at the first valid local time.</remarks>
    private static DateTime ToUtc(DateTime localMidnight, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
