namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

/// <summary>Local-calendar arithmetic for the usage summary: which local day a UTC moment falls on in a zone,
/// and how days roll up into day, ISO-week and month buckets.</summary>
public static class UsageCalendar
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    // Local offsets run from UTC-12 to UTC+14, so this much padding either side of the local days' naive UTC
    // bounds holds every moment of them in any zone. Slots outside the local days are dropped after conversion.
    private static readonly TimeSpan OffsetPadding = TimeSpan.FromHours(14);
    private const int MaxZoneIdLength = 64;

    public static bool IsGranularity(string? value) => value is Day or Week or Month;

    // The id comes from the query string and becomes a file path under the zoneinfo root on Linux, where a
    // directory such as "America" throws SecurityException from FindSystemTimeZoneById. The Try form turns
    // every failure into false. No real IANA id is anywhere near 64 characters.
    public static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxZoneIdLength
            || !TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found))
            return false;
        zone = found;
        return true;
    }

    /// <summary>A UTC window, [from, to), that holds every moment of the local days from..to in any zone.
    /// Converting local midnight itself is avoided: where DST starts at midnight, that time does not exist.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) QueryWindow(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - OffsetPadding,
        to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) + OffsetPadding);

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    /// <summary>The day itself, the Monday of its ISO week, or the 1st of its month.</summary>
    public static DateOnly BucketStart(DateOnly date, string granularity) => granularity switch
    {
        Day => date,
        Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        Month => new DateOnly(date.Year, date.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null),
    };

    /// <summary>Every bucket start from the one holding <paramref name="from"/> to the one holding
    /// <paramref name="to"/>, ascending.</summary>
    public static IReadOnlyList<DateOnly> BucketStarts(DateOnly from, DateOnly to, string granularity)
    {
        var starts = new List<DateOnly>();
        for (var start = BucketStart(from, granularity); start <= to; start = Next(start, granularity))
            starts.Add(start);
        return starts;
    }

    private static DateOnly Next(DateOnly start, string granularity) => granularity switch
    {
        Day => start.AddDays(1),
        Week => start.AddDays(7),
        _ => start.AddMonths(1),
    };
}
