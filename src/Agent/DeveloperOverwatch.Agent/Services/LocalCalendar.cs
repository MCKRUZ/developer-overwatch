namespace DeveloperOverwatch.Agent.Services;

/// <summary>Local calendar boundaries as UTC instants: midnight today, Monday midnight, and midnight on
/// the first of the month, in the given zone.</summary>
public static class LocalCalendar
{
    public static DateTime TodayStartUtc(DateTime nowUtc, TimeZoneInfo zone) =>
        ToUtc(LocalToday(nowUtc, zone), zone);

    public static DateTime WeekStartUtc(DateTime nowUtc, TimeZoneInfo zone)
    {
        var localToday = LocalToday(nowUtc, zone);
        var daysSinceMonday = ((int)localToday.DayOfWeek + 6) % 7;
        return ToUtc(localToday.AddDays(-daysSinceMonday), zone);
    }

    public static DateTime MonthStartUtc(DateTime nowUtc, TimeZoneInfo zone)
    {
        var localToday = LocalToday(nowUtc, zone);
        return ToUtc(new DateTime(localToday.Year, localToday.Month, 1), zone);
    }

    private static DateTime LocalToday(DateTime nowUtc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;

    /// <remarks>In a few zones a DST change skips local midnight; the period then starts at the first valid local time.</remarks>
    private static DateTime ToUtc(DateTime localMidnight, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
