using System.Text.Json;
using DeveloperOverwatch.Agent.Cli;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DeveloperOverwatch.Agent.Tests.Cli;

public sealed class UsageCommandTests : IDisposable
{
    // Fixed UTC-5 with no DST: local days differ from UTC days on every machine, so a report that
    // bucketed by UTC date would put the 23:59-local rows on the wrong day.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-05", TimeSpan.FromHours(-5), "Test-05", "Test-05");

    // Wednesday 2026-11-11 15:00 local. The week started Monday 2026-11-09.
    private static readonly DateTime NowUtc = AtLocal(2026, 11, 11, 15, 0);

    // Flat test prices: any unlisted model ("m-a") costs $1 and "m-b" $10 per million tokens of any kind.
    private const string TestPricing = """
        "pricing": { "asOf": "2030-01-01", "models": {
            "default": { "inputPerMillionUsd": 1, "outputPerMillionUsd": 1, "cacheWritePerMillionUsd": 1, "cacheReadPerMillionUsd": 1 },
            "m-b": { "inputPerMillionUsd": 10, "outputPerMillionUsd": 10, "cacheWritePerMillionUsd": 10, "cacheReadPerMillionUsd": 10 } } }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-usage-cli-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _log;

    public UsageCommandTests(ITestOutputHelper log)
    {
        _log = log;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Usage_RowsOnLocalBoundaries_CountInTheRightPeriods()
    {
        await SeedBoundaryRowsAsync();

        var (exit, report, _) = await RunAsync(Config());

        exit.Should().Be(0);
        Fields(report, "Today").Should().Equal("Today", "100,000", "2,000,000", "0", "0", "2,100,000", "$3.00");
        Fields(report, "This week").Should().Equal("This", "week", "1,100,000", "2,000,000", "0", "0", "3,100,000", "$4.00");
        Fields(report, "This month").Should().Equal("This", "month", "1,100,000", "2,000,000", "3,000,000", "0", "6,100,000", "$34.00");
        report.Should().Contain("times are local, UTC-05:00").And.NotContain("Budget");
        report.All(c => c < 128).Should().BeTrue("the report must be ASCII-safe");
    }

    [Fact]
    public async Task Usage_MixedModels_SplitsThisMonthByModelAtEachModelsPrice()
    {
        await SeedBoundaryRowsAsync();

        var (_, report, _) = await RunAsync(Config());

        Fields(report, "m-b").Should().Equal("m-b", "3,100,000", "$31.00");
        Fields(report, "m-a").Should().Equal("m-a", "3,000,000", "$3.00");
    }

    [Fact]
    public async Task Usage_ActivityInTheLastFiveHours_ShowsTheCurrentWindow()
    {
        await SeedBoundaryRowsAsync();

        var (_, report, _) = await RunAsync(Config());

        // 04:59 and 05:00 UTC open and fill an earlier window; 19:00 UTC (14:00 local) opens the current one.
        report.Should().Contain("  Started 2026-11-11 14:00, resets 2026-11-11 19:00")
            .And.Contain("  100,000 tokens, est. $1.00 so far");
    }

    [Fact]
    public async Task Usage_LastActivityOverFiveHoursAgo_SaysNoActiveWindow()
    {
        await SeedAsync(Row("old", NowUtc.AddHours(-6), "m-a", input: 1_000));

        var (_, report, _) = await RunAsync(Config());

        report.Should().Contain("Current 5-hour window: no active window");
    }

    [Fact]
    public async Task Usage_BudgetConfigured_ShowsSpentAgainstEachLimit()
    {
        await SeedBoundaryRowsAsync();

        var (_, report, _) = await RunAsync(Config(TestPricing + """, "budget": { "dailyUsd": 6, "monthlyUsd": 68 }"""));

        Fields(report, "Daily").Should().Equal("Daily", "$3.00", "/", "$6.00", "(50.0%)");
        Fields(report, "Monthly").Should().Equal("Monthly", "$34.00", "/", "$68.00", "(50.0%)");
    }

    [Fact]
    public async Task Usage_PricingOverrideInConfig_ReplacesTheBuiltInPrices()
    {
        await SeedAsync(Row("opus", NowUtc.AddMinutes(-10), "claude-opus-5-5", input: 1_000_000));

        var (_, builtIn, _) = await RunAsync(Config(""));
        var (_, overridden, _) = await RunAsync(Config());

        Fields(builtIn, "Today").Last().Should().Be("$4.00");
        builtIn.Should().Contain("Estimates at API list prices as of 2026-11-09. Mode: Local.");
        Fields(overridden, "Today").Last().Should().Be("$1.00", "the override has no opus entry, so its default applies");
        overridden.Should().Contain("as of 2030-01-01.");
    }

    [Fact]
    public async Task Usage_EmptyTable_SaysNoUsageAndStillPrintsTheFooter()
    {
        var (exit, report, _) = await RunAsync(Config());

        exit.Should().Be(0);
        report.Should().Contain("No usage recorded yet.")
            .And.Contain("Estimates at API list prices as of 2030-01-01. Mode: Local.")
            .And.NotContain("Today");
    }

    [Fact]
    public async Task Usage_PricingOverrideWithoutDefault_FailsWithAMessage()
    {
        var noDefault = """
            "pricing": { "asOf": "2030-01-01", "models": { "m-b": { "inputPerMillionUsd": 10 } } }
            """;

        var (exit, report, error) = await RunAsync(Config(noDefault));

        exit.Should().Be(1);
        error.Should().Contain("'pricing' section in config.json is invalid");
        report.Should().BeEmpty();
    }

    // Rows on each side of every local boundary, plus one inside the current window.
    private Task SeedBoundaryRowsAsync() => SeedAsync(
        Row("last-month", AtLocal(2026, 10, 31, 23, 59), "m-b", cacheRead: 4_000_000),
        Row("last-week", AtLocal(2026, 11, 8, 23, 59), "m-b", cacheWrite: 3_000_000),
        Row("yesterday", AtLocal(2026, 11, 10, 23, 59), "m-a", input: 1_000_000),
        Row("today-start", AtLocal(2026, 11, 11, 0, 0), "m-a", output: 2_000_000),
        Row("recent", AtLocal(2026, 11, 11, 14, 0), "m-b", input: 100_000));

    private async Task SeedAsync(params CapturedTurn[] turns)
    {
        using var store = new UsageStore(Path.Combine(_dir, "queue.db"));
        await store.InitializeAsync();
        await store.RecordTurnsAsync("session-1", turns);
    }

    private static CapturedTurn Row(
        string id, DateTime utc, string model, int? input = null, int? output = null, int? cacheWrite = null, int? cacheRead = null) =>
        new(id, "assistant", null, utc, input, output, cacheWrite, cacheRead, model);

    /// <summary>A wall-clock time in <see cref="Zone"/>, as UTC.</summary>
    private static DateTime AtLocal(int year, int month, int day, int hour, int minute) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(5);

    private AgentConfig Config(string sections = TestPricing)
    {
        var path = Path.Combine(_dir, "config.json");
        var extra = sections.Length == 0 ? "" : ", " + sections;
        File.WriteAllText(path, $$"""{ "mode": "Local"{{extra}} }""");
        // config.json cannot move the agent's folder, so the test points it at the temp dir in code.
        var loaded = AgentConfig.Load(path);
        return new AgentConfig { Mode = loaded.Mode, Pricing = loaded.Pricing, Budget = loaded.Budget, OverwatchDir = _dir };
    }

    private async Task<(int Exit, string Report, string Error)> RunAsync(AgentConfig config)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CommandDispatcher.RunAsync(["usage"], output, error, config, new ZonedTimeProvider(NowUtc, Zone));
        _log.WriteLine(output.ToString());
        return (exit, output.ToString(), error.ToString());
    }

    private static string[] Fields(string report, string label) =>
        report.Split('\n')
            .Select(line => line.Trim())
            .Single(line => line.StartsWith(label + " ", StringComparison.Ordinal))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
