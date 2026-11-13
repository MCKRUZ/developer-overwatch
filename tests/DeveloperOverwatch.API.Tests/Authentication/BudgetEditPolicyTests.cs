namespace DeveloperOverwatch.API.Tests.Authentication;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

/// <summary>Budgets and plans change what the whole team is measured against, so editing them is narrower than
/// reading: the Lead app role with sign-in on; anyone in local development without sign-in; nobody on any other
/// server without sign-in. Each case deletes an id that does not exist: 404 means the policy let the request
/// through, 403 means it refused, and the shared database is never changed.</summary>
public static class BudgetEditProbe
{
    public static Task<HttpResponseMessage> DeleteMissingBudgetAsync(HttpClient client) =>
        client.DeleteAsync($"/api/budgets/{Guid.NewGuid()}");

    public static async Task<bool> CanEditBudgetsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("canEditBudgets").GetBoolean();
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public class BudgetEditPolicyOidcTests : IClassFixture<OidcWebApplicationFactory>
{
    private readonly OidcWebApplicationFactory _factory;

    public BudgetEditPolicyOidcTests(OidcWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteBudget_LeadRole_PassesAuthorization()
    {
        var client = _factory.CreateClient().WithBearer(OidcWebApplicationFactory.MintToken("lead-user", "Lead"));

        var response = await BudgetEditProbe.DeleteMissingBudgetAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BudgetEditProbe.CanEditBudgetsAsync(client)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBudget_SignedInWithoutLeadRole_Returns403()
    {
        var client = _factory.CreateClient()
            .WithBearer(OidcWebApplicationFactory.MintToken("plain-user", "Reader"));

        var response = await BudgetEditProbe.DeleteMissingBudgetAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BudgetEditProbe.CanEditBudgetsAsync(client)).Should().BeFalse();
    }

    // Reads stay on the dashboard policy: a signed-in user without the role still sees budgets and plans.
    [Theory]
    [InlineData("/api/budgets")]
    [InlineData("/api/developer-plans")]
    public async Task Read_SignedInWithoutLeadRole_Returns200(string url)
    {
        var client = _factory.CreateClient().WithBearer(OidcWebApplicationFactory.MintToken("plain-user"));

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public class BudgetEditPolicyNoSignInTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public BudgetEditPolicyNoSignInTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    // The shared factory runs in Development, the way a developer runs the server locally.
    [Fact]
    public async Task DeleteBudget_NoSignInInDevelopment_PassesAuthorization()
    {
        var client = _factory.CreateClient();

        var response = await BudgetEditProbe.DeleteMissingBudgetAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BudgetEditProbe.CanEditBudgetsAsync(client)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBudget_NoSignInInProduction_Returns403AndReadsStillWork()
    {
        using var baseFactory = new OverwatchWebApplicationFactory();
        using var production = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient();

        var response = await BudgetEditProbe.DeleteMissingBudgetAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BudgetEditProbe.CanEditBudgetsAsync(client)).Should().BeFalse();
        (await client.GetAsync("/api/budgets")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/developer-plans")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
