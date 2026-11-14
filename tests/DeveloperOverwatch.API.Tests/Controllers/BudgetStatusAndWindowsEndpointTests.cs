namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.API.Tests.Controllers.BudgetsControllerTests;

/// <summary>GET /api/budgets/status through the real pipeline. The dashboard reads the exact JSON shape: nullable
/// members present as explicit nulls, times as UTC with a trailing Z, enums by name. Nothing is ingested in this
/// class, so the seeded team daily budget has no spend.</summary>
public class BudgetStatusEndpointTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BudgetStatusEndpointTests(OverwatchWebApplicationFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("")]
    [InlineData("?tz=")]
    [InlineData("?tz=Mars/Olympus_Mons")]
    [InlineData("?tz=America")]
    public async Task GetStatus_MissingOrUnknownTz_Returns400ProblemDetails(string query)
    {
        var messages = await ValidationMessagesAsync(await _client.GetAsync($"/api/budgets/status{query}"));

        messages.Should().Equal("'tz' must be a known IANA time zone id.");
    }

    [Fact]
    public async Task GetStatus_SeededTeamBudget_ReturnsTheContractShape()
    {
        var response = await _client.GetAsync("/api/budgets/status?tz=America/New_York");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = (await JsonAsync(response)).EnumerateArray().First();
        team.GetProperty("budget").GetProperty("scope").GetString().Should().Be("Team");
        team.GetProperty("budget").GetProperty("developerEmail").ValueKind.Should().Be(JsonValueKind.Null);
        team.GetProperty("periodStartUtc").GetString().Should().EndWith("Z");
        team.GetProperty("periodEndUtc").GetString().Should().EndWith("Z");
        team.GetProperty("spentUsd").GetDecimal().Should().Be(0m);
        team.GetProperty("remainingUsd").GetDecimal().Should().Be(50m);
        team.GetProperty("usedFraction").GetDecimal().Should().Be(0m);
        team.GetProperty("level").GetString().Should().Be("Ok");
        var pace = team.GetProperty("pace");
        pace.GetProperty("usedFraction").ValueKind.Should().Be(JsonValueKind.Number);
        pace.GetProperty("elapsedFraction").ValueKind.Should().Be(JsonValueKind.Number);
        pace.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        team.GetProperty("forecast").GetProperty("kind").GetString().Should().Be("NoSpendYet");
        team.GetProperty("forecast").GetProperty("runsOutAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

/// <summary>GET /api/usage/windows and /api/usage/plan-value through the real pipeline, on the real clock. The class
/// shares one database, so each test uses its own developer emails.</summary>
public class UsageWindowsAndPlanValueEndpointTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsageWindowsAndPlanValueEndpointTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetWindows_PlanHolderWithoutUsage_WritesAnExplicitNullWindow()
    {
        await CreatePlan("idle-window@team.dev", windowLimitUsd: 10m);

        var row = await WindowRow("idle-window@team.dev");

        row.GetProperty("planName").GetString().Should().Be("Max");
        row.GetProperty("windowLimitUsd").GetDecimal().Should().Be(10m);
        row.GetProperty("window").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetWindows_UsageWithoutAPlan_WritesExplicitNullsForEverythingTheLimitWouldGive()
    {
        await Ingest("noplan-window@team.dev");

        var row = await WindowRow("noplan-window@team.dev");

        row.GetProperty("planName").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("windowLimitUsd").ValueKind.Should().Be(JsonValueKind.Null);
        var window = row.GetProperty("window");
        window.GetProperty("startUtc").GetString().Should().EndWith("Z");
        window.GetProperty("resetsAtUtc").GetString().Should().EndWith("Z");
        window.GetProperty("spentUsd").ValueKind.Should().Be(JsonValueKind.Number);
        window.GetProperty("tokens").GetInt64().Should().Be(40);
        foreach (var name in new[] { "usedFraction", "level", "pace", "forecast" })
            window.GetProperty(name).ValueKind.Should().Be(JsonValueKind.Null, name);
    }

    [Fact]
    public async Task GetWindows_UsageWithALimit_WritesFractionLevelPaceAndForecast()
    {
        await CreatePlan("limit-window@team.dev", windowLimitUsd: 10m);
        await Ingest("limit-window@team.dev");

        var window = (await WindowRow("limit-window@team.dev")).GetProperty("window");

        window.GetProperty("usedFraction").ValueKind.Should().Be(JsonValueKind.Number);
        window.GetProperty("level").GetString().Should().BeOneOf("Ok", "Amber", "Red");
        window.GetProperty("pace").GetProperty("status").GetString().Should().BeOneOf("Ahead", "OnPace", "Behind");
        var forecast = window.GetProperty("forecast");
        forecast.GetProperty("kind").ValueKind.Should().Be(JsonValueKind.String);
        forecast.TryGetProperty("runsOutAt", out _).Should().BeTrue("a null run-out time is written, not omitted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?tz=Mars/Olympus_Mons")]
    public async Task GetPlanValue_MissingOrUnknownTz_Returns400ProblemDetails(string query)
    {
        var messages = await ValidationMessagesAsync(await _client.GetAsync($"/api/usage/plan-value{query}"));

        messages.Should().Equal("'tz' must be a known IANA time zone id.");
    }

    [Fact]
    public async Task GetPlanValue_PlanWithoutUsage_ReturnsTheContractShape()
    {
        await CreatePlan("value@team.dev", windowLimitUsd: null);

        var response = await _client.GetAsync("/api/usage/plan-value?tz=America/New_York");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await JsonAsync(response)).EnumerateArray()
            .Single(r => r.GetProperty("developerEmail").GetString() == "value@team.dev");
        row.GetProperty("planName").GetString().Should().Be("Max");
        row.GetProperty("monthlyPriceUsd").GetDecimal().Should().Be(200m);
        row.GetProperty("monthStartUtc").GetString().Should().EndWith("Z");
        row.GetProperty("apiValueUsd").GetDecimal().Should().Be(0m);
        row.GetProperty("valueMultiple").GetDecimal().Should().Be(0m);
    }

    private async Task<JsonElement> WindowRow(string email)
    {
        var response = await _client.GetAsync("/api/usage/windows");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await JsonAsync(response)).EnumerateArray().Single(r => r.GetProperty("developerEmail").GetString() == email);
    }

    private async Task CreatePlan(string email, decimal? windowLimitUsd)
    {
        var response = await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = email, planName = "Max", monthlyPriceUsd = 200m, windowLimitUsd });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task Ingest(string developer)
    {
        var sessionId = Guid.NewGuid().ToString();
        var at = DateTime.UtcNow.AddMinutes(-10);
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
            },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
