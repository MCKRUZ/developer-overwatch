namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using FluentAssertions;
using Xunit;

/// <summary>A browser can report an old zone name such as Asia/Calcutta. On Ubuntu noble without tzdata-legacy
/// that zone file is missing, so the lookup must fall back to the link list in tzdata.zi. The host here is a
/// fake that knows only the current names, which is what such a machine looks like to .NET.</summary>
public sealed class UsageCalendarZoneTests
{
    // Lines copied from the tzdata.zi in Ubuntu noble's tzdata 2026f-0ubuntu0.24.04.1 package.
    private static readonly IReadOnlyDictionary<string, string> NobleLinks = TzdataLinks.Parse(
    [
        "Z Asia/Kolkata 5:53:28 - LMT 1854 Jun 28",
        "L America/New_York US/Eastern",
        "L Asia/Kolkata Asia/Calcutta",
        "L Europe/Kyiv Europe/Kiev",
        "L Pacific/Kanton Pacific/Enderbury",
    ]);

    private static readonly HashSet<string> CurrentNames = ["America/New_York", "Asia/Kolkata", "Europe/Kyiv"];

    private static TimeZoneInfo? HostWithoutLegacyNames(string id) => CurrentNames.Contains(id)
        ? TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id)
        : null;

    [Theory]
    [InlineData("Asia/Calcutta", "Asia/Kolkata")]
    [InlineData("Europe/Kiev", "Europe/Kyiv")]
    [InlineData("US/Eastern", "America/New_York")]
    public void TryFindZone_LegacyNameMissingOnHost_ResolvesThroughTzdataLink(string legacy, string current)
    {
        var found = UsageCalendar.TryFindZone(legacy, HostWithoutLegacyNames, NobleLinks, out var zone);

        found.Should().BeTrue();
        zone.Id.Should().Be(current);
    }

    [Fact]
    public void TryFindZone_CurrentName_IsFoundDirectly()
    {
        UsageCalendar.TryFindZone("Asia/Kolkata", HostWithoutLegacyNames, NobleLinks, out var zone)
            .Should().BeTrue();
        zone.Id.Should().Be("Asia/Kolkata");
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")] // in neither the host nor the link list
    [InlineData("Pacific/Enderbury")] // linked, but the link's target is missing on the host
    public void TryFindZone_UnresolvableName_ReturnsFalse(string id) =>
        UsageCalendar.TryFindZone(id, HostWithoutLegacyNames, NobleLinks, out _).Should().BeFalse();

    [Fact]
    public void TryFindZone_OverLongNameEvenIfLinked_ReturnsFalse()
    {
        var longName = new string('A', 65);
        var links = TzdataLinks.Parse([$"L Asia/Kolkata {longName}"]);

        UsageCalendar.TryFindZone(longName, HostWithoutLegacyNames, links, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_KeepsOnlyLinkLines()
    {
        NobleLinks.Should().HaveCount(4).And.ContainKey("Asia/Calcutta").WhoseValue.Should().Be("Asia/Kolkata");
        NobleLinks.Should().NotContainKey("Asia/Kolkata");
    }
}
