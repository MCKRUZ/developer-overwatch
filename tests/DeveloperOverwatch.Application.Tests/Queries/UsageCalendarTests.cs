namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>Where a local day starts in UTC. Budgets count spend from that instant, so a day that starts an hour
/// late or early moves an hour of spend between days.</summary>
public sealed class UsageCalendarTests
{
    [Fact]
    public void LocalMidnightUtc_OrdinaryNewYorkDay_IsMidnightAtTheStandardOffset()
    {
        UsageCalendar.LocalMidnightUtc(new(2026, 11, 10), Zone("America/New_York"))
            .Should().Be(Utc(2026, 11, 10, 5)).And.BeIn(DateTimeKind.Utc);
    }

    // DST starts at 02:00 in New York, so midnight exists, but the day is 23 hours long.
    [Fact]
    public void LocalMidnightUtc_NewYorkSpringForwardDay_StartsAtMinusFiveAndTheNextDayAtMinusFour()
    {
        var zone = Zone("America/New_York");

        UsageCalendar.LocalMidnightUtc(new(2026, 3, 8), zone).Should().Be(Utc(2026, 3, 8, 5));
        UsageCalendar.LocalMidnightUtc(new(2026, 3, 9), zone).Should().Be(Utc(2026, 3, 9, 4));
    }

    // Santiago, Havana and Beirut move their clocks forward at midnight itself: 00:00 does not exist that day,
    // so the day starts at the first instant that does, 01:00 at the new offset.
    [Theory]
    [InlineData("America/Santiago", 2026, 9, 6, 2026, 9, 6, 4)]
    [InlineData("America/Havana", 2026, 3, 8, 2026, 3, 8, 5)]
    [InlineData("Asia/Beirut", 2026, 3, 29, 2026, 3, 28, 22)]
    public void LocalMidnightUtc_MidnightSkippedByDst_IsTheFirstInstantThatExists(
        string id, int year, int month, int day, int utcYear, int utcMonth, int utcDay, int utcHour)
    {
        var zone = Zone(id);
        var date = new DateOnly(year, month, day);
        zone.IsInvalidTime(date.ToDateTime(TimeOnly.MinValue))
            .Should().BeTrue("this case must exercise the DST gap, or it tests nothing");

        UsageCalendar.LocalMidnightUtc(date, zone)
            .Should().Be(Utc(utcYear, utcMonth, utcDay, utcHour)).And.BeIn(DateTimeKind.Utc);
    }

    // The Azores fall back from 01:00 to 00:00, so midnight happens twice: at 00:00 UTC (summer, +0) and again
    // at 01:00 UTC (winter, -1). The day starts at the first.
    [Fact]
    public void LocalMidnightUtc_MidnightRepeatedByDst_IsTheEarlierInstant()
    {
        var zone = Zone("Atlantic/Azores");
        var date = new DateOnly(2026, 10, 25);
        zone.IsAmbiguousTime(date.ToDateTime(TimeOnly.MinValue))
            .Should().BeTrue("this case must exercise the repeated hour, or it tests nothing");

        UsageCalendar.LocalMidnightUtc(date, zone)
            .Should().Be(Utc(2026, 10, 25, 0)).And.BeIn(DateTimeKind.Utc);
    }

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}
