namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>GET /api/usage/summary through the real pipeline: the dashboard depends on the exact JSON shape, and
/// each bad input must be refused with the message for its own rule, not a generic 400.</summary>
public class UsageSummaryEndpointTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private const string Valid = "from=2026-11-01&to=2026-11-10&granularity=day&timeZone=America/New_York";

    private readonly OverwatchWebApplicationFactory _factory;

    // 65 characters: one past the cap, so an id is never handed to a file-system lookup at any length.
    private const string TooLongZone = "Abcdefghij/Abcdefghij/Abcdefghij/Abcdefghij/Abcdefghij/Abcdefghij";

    public UsageSummaryEndpointTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("from=2026-11-10&to=2026-11-01&granularity=day&timeZone=Etc/UTC", "'from' must not be after 'to'")]
    [InlineData("from=2025-11-01&to=2026-11-02&granularity=day&timeZone=Etc/UTC", "at most 366 days")]
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=hour&timeZone=Etc/UTC", "'granularity' must be day, week or month")]
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=Day&timeZone=Etc/UTC", "'granularity' must be day, week or month")]
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=day&timeZone=Mars/Olympus_Mons", "known IANA time zone")]
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=day", "known IANA time zone")]
    // On Linux "America" is a directory under the zoneinfo root, not a zone; reading it must be a 400, not a 500.
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=day&timeZone=America", "known IANA time zone")]
    [InlineData("from=2026-11-01&to=2026-11-10&granularity=day&timeZone=" + TooLongZone, "known IANA time zone")]
    [InlineData("to=2026-11-10&granularity=day&timeZone=Etc/UTC", "From")]
    [InlineData("from=2026-11-01&granularity=day&timeZone=Etc/UTC", "To")]
    public async Task GetSummary_InvalidInput_Returns400NamingTheBrokenRule(string query, string message)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/usage/summary?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString())
            .Should().ContainSingle(e => e!.Contains(message));
    }

    // Control for the 400s, and the boundary of the range rule: 366 days counting both ends is allowed.
    [Fact]
    public async Task GetSummary_Exactly366Days_Returns200()
    {
        var response = await _factory.CreateClient()
            .GetAsync("/api/usage/summary?from=2025-11-02&to=2026-11-02&granularity=month&timeZone=Etc/UTC");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_AfterIngestingTurns_ReturnsTheContractShape()
    {
        var sessionId = Guid.NewGuid().ToString();
        var developer = $"{sessionId[..8]}@team.dev";
        var at = new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);
        await Ingest(sessionId, developer, at);

        var response = await _factory.CreateClient()
            .GetAsync($"/api/usage/summary?{Valid}&developer={Uri.EscapeDataString(developer)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("granularity").GetString().Should().Be("day");
        root.GetProperty("from").GetString().Should().Be("2026-11-01");
        root.GetProperty("pricesAsOf").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        root.GetProperty("totals").GetProperty("outputTokens").GetInt64().Should().Be(40);
        root.GetProperty("buckets").GetArrayLength().Should().Be(10);
        var day = root.GetProperty("buckets").EnumerateArray().Single(b => b.GetProperty("turnCount").GetInt32() > 0);
        day.GetProperty("start").GetString().Should().Be("2026-11-05");
        root.GetProperty("buckets")[0].TryGetProperty("cacheHitRate", out _)
            .Should().BeFalse("an empty day has no hit rate, and nulls are omitted");
        var mainThread = root.GetProperty("splits").GetProperty("subagent").EnumerateArray()
            .Single(r => r.GetProperty("label").GetString() == "main thread");
        mainThread.TryGetProperty("key", out _).Should().BeFalse();
        mainThread.GetProperty("firstAt").GetString().Should().Be("2026-11-05T15:00:00Z",
            "an unmarked time would be read as the viewer's local time");
        root.GetProperty("burn").GetProperty("lookbackMinutes").GetInt32().Should().Be(15);
    }

    private async Task Ingest(string sessionId, string developer, DateTime at)
    {
        var client = _factory.CreateAuthenticatedClient();
        (await client.PostAsJsonAsync("/api/sessions/start", new
        {
            SessionId = sessionId, DeveloperEmail = developer, RepoPath = "/src/meter", StartedAt = at,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", new
        {
            SessionId = sessionId,
            ConversationTurns = new[]
            {
                new { Id = Guid.NewGuid().ToString(), TurnIndex = 0, Role = "assistant", OutputTokens = 40, Model = "claude-sonnet", Timestamp = at },
                new { Id = Guid.NewGuid().ToString(), TurnIndex = 1, Role = "assistant", OutputTokens = 0, Model = "claude-sonnet", Timestamp = at.AddMinutes(1) },
            },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
