using DeveloperOverwatch.Agent.Services;
using DeveloperOverwatch.Agent.Tests.Cli;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The usage command's "This month" reads local rows, so the purge must never cut into the
/// current local month, while still keeping at least the retention period.</summary>
public sealed class UsageRetentionTests : IDisposable
{
    // Five hours behind UTC, so the local month starts at 05:00 UTC on the 1st.
    private static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-retention-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Purge_OnThe31st_KeepsTheWholeCurrentLocalMonth()
    {
        var now = Utc(2026, 10, 31, 12);
        var rows = await PurgeAndReadAsync(now,
            Turn("first-local-hour", Utc(2026, 10, 1, 6)),
            Turn("last-local-september-hour", Utc(2026, 10, 1, 4)));

        rows.Should().Equal("first-local-hour");
    }

    [Fact]
    public async Task Purge_EarlyInTheMonth_StillKeepsThirtyDays()
    {
        var now = Utc(2026, 11, 5, 12);
        var rows = await PurgeAndReadAsync(now,
            Turn("previous-month-29-days-ago", now.AddDays(-29)),
            Turn("31-days-ago", now.AddDays(-31)));

        rows.Should().Equal("previous-month-29-days-ago");
    }

    private async Task<List<string>> PurgeAndReadAsync(DateTime nowUtc, params CapturedTurn[] turns)
    {
        using var store = new UsageStore(Path.Combine(_dir, $"{Guid.NewGuid():N}.db"), new ZonedTimeProvider(nowUtc, UtcMinus5));
        await store.InitializeAsync();
        await store.RecordTurnsAsync("s1", turns);

        await store.PurgeAsync(TimeSpan.FromDays(30));

        var rows = await store.GetUsageAsync(nowUtc.AddDays(-400), nowUtc.AddDays(1));
        return rows.Select(r => r.SourceId).ToList();
    }

    private static CapturedTurn Turn(string sourceId, DateTime timestamp) =>
        new(sourceId, "assistant", null, timestamp, InputTokens: 10, OutputTokens: 5, Model: "claude-opus-4-1");

    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
