namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>Budget CRUD through the real API on the migrated in-memory SQLite database. The factory runs in
/// Development without sign-in, so edits are allowed, and its database starts with the seeded $50 team daily
/// budget. The class shares one database, so each test uses its own developer email.</summary>
public class BudgetsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BudgetsControllerTests(OverwatchWebApplicationFactory factory) => _client = factory.CreateClient();

    internal static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    internal static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Every message of an RFC 7807 validation problem, which is what the dashboard shows.</summary>
    internal static async Task<string[]> ValidationMessagesAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var errors = (await JsonAsync(response)).GetProperty("errors");
        return errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray()).Select(m => m.GetString()!).ToArray();
    }

    [Fact]
    public async Task GetBudgets_SeededTeamBudget_WritesNullEmailAndUtcTimes()
    {
        var budgets = await JsonAsync(await _client.GetAsync("/api/budgets"));

        var team = budgets.EnumerateArray().First();
        team.GetProperty("scope").GetString().Should().Be("Team");
        team.GetProperty("period").GetString().Should().Be("Daily");
        team.GetProperty("amountUsd").GetDecimal().Should().Be(50m);
        team.GetProperty("developerEmail").ValueKind.Should().Be(JsonValueKind.Null);
        team.GetProperty("createdAt").GetString().Should().EndWith("Z");
    }

    [Fact]
    public async Task CreateBudget_Developer_Returns201WithTrimmedEmailAndAppearsInList()
    {
        var response = await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Developer", developerEmail = " create@team.dev ", period = "Monthly", amountUsd = 250.5m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await JsonAsync(response);
        created.GetProperty("developerEmail").GetString().Should().Be("create@team.dev");
        created.GetProperty("amountUsd").GetDecimal().Should().Be(250.5m);
        response.Headers.Location!.ToString().Should().Be($"/api/budgets/{created.GetProperty("id").GetString()}");
        var list = await JsonAsync(await _client.GetAsync("/api/budgets"));
        list.EnumerateArray().Should().Contain(b => b.GetProperty("id").GetString() == created.GetProperty("id").GetString());
    }

    [Fact]
    public async Task CreateBudget_SeededTeamDaily_Returns409()
    {
        var response = await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Team", period = "Daily", amountUsd = 80m });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateBudget_SameDeveloperWithStraySpaces_Returns409()
    {
        var first = await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Developer", developerEmail = "dupe@team.dev", period = "Daily", amountUsd = 5m });
        var second = await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Developer", developerEmail = " dupe@team.dev ", period = "Daily", amountUsd = 6m });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateBudget_InvalidValues_Returns400ProblemWithEveryMessage()
    {
        var response = await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Team", developerEmail = "", period = "Monthly", amountUsd = 0m });

        var messages = await ValidationMessagesAsync(response);
        messages.Should().Contain("A team budget has no developer email.");
        messages.Should().Contain(m => m.Contains("Amount Usd"));
    }

    [Theory]
    [InlineData("""{ "scope": "Team", "period": "Monthly" }""")]
    [InlineData("""{ "period": "Monthly", "amountUsd": 10 }""")]
    [InlineData("""{ "scope": "Weekly", "period": "Monthly", "amountUsd": 10 }""")]
    [InlineData("""{ "scope": 5, "period": "Monthly", "amountUsd": 10 }""")]
    public async Task CreateBudget_MissingOrUnknownFields_Returns400Problem(string json)
    {
        var response = await _client.PostAsync("/api/budgets", Body(json));

        (await ValidationMessagesAsync(response)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpdateAndDeleteBudget_ChangeAmountThenRemove()
    {
        var created = await JsonAsync(await _client.PostAsJsonAsync("/api/budgets",
            new { scope = "Developer", developerEmail = "edit@team.dev", period = "Daily", amountUsd = 5m }));
        var url = $"/api/budgets/{created.GetProperty("id").GetString()}";

        var updated = await _client.PutAsJsonAsync(url, new { amountUsd = 12.25m });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(updated)).GetProperty("amountUsd").GetDecimal().Should().Be(12.25m);
        (await _client.PutAsJsonAsync(url, new { amountUsd = -1m })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync(url, new { amountUsd = 3m })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class DeveloperPlansControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DeveloperPlansControllerTests(OverwatchWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task CreatePlan_NoWindowLimit_Returns201WithExplicitNull()
    {
        var response = await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = " plan@team.dev ", planName = " Max 5x ", monthlyPriceUsd = 100m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = await BudgetsControllerTests.JsonAsync(response);
        plan.GetProperty("developerEmail").GetString().Should().Be("plan@team.dev");
        plan.GetProperty("planName").GetString().Should().Be("Max 5x");
        plan.GetProperty("windowLimitUsd").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("updatedAt").GetString().Should().EndWith("Z");
    }

    [Fact]
    public async Task CreatePlan_EmailAlreadyHasPlan_Returns409()
    {
        var first = await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = "twice@team.dev", planName = "Pro", monthlyPriceUsd = 20m });
        var second = await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = " twice@team.dev", planName = "Max", monthlyPriceUsd = 100m, windowLimitUsd = 40m });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreatePlan_InvalidValues_Returns400ProblemWithEveryMessage()
    {
        var response = await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = "nope", planName = new string('n', 101), monthlyPriceUsd = 0m, windowLimitUsd = 0m });

        (await BudgetsControllerTests.ValidationMessagesAsync(response)).Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task GetPlans_OrdersByEmail()
    {
        await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = "zz-order@team.dev", planName = "Pro", monthlyPriceUsd = 20m });
        await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = "aa-order@team.dev", planName = "Pro", monthlyPriceUsd = 20m });

        var emails = (await BudgetsControllerTests.JsonAsync(await _client.GetAsync("/api/developer-plans")))
            .EnumerateArray().Select(p => p.GetProperty("developerEmail").GetString()).ToList();

        emails.Should().BeInAscendingOrder(StringComparer.Ordinal);
        emails.Should().Contain(["aa-order@team.dev", "zz-order@team.dev"]);
    }

    [Fact]
    public async Task UpdateAndDeletePlan_ReplaceValuesThenRemove()
    {
        var created = await BudgetsControllerTests.JsonAsync(await _client.PostAsJsonAsync("/api/developer-plans",
            new { developerEmail = "edit-plan@team.dev", planName = "Pro", monthlyPriceUsd = 20m }));
        var url = $"/api/developer-plans/{created.GetProperty("id").GetString()}";

        var updated = await _client.PutAsJsonAsync(url, new { planName = "Max", monthlyPriceUsd = 200m, windowLimitUsd = 55.5m });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await BudgetsControllerTests.JsonAsync(updated);
        plan.GetProperty("planName").GetString().Should().Be("Max");
        plan.GetProperty("windowLimitUsd").GetDecimal().Should().Be(55.5m);
        (await _client.PutAsJsonAsync(url, new { planName = " ", monthlyPriceUsd = 20m }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync(url, new { planName = "Pro", monthlyPriceUsd = 20m }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
