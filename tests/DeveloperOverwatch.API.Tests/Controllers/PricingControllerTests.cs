namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class PricingControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public PricingControllerTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetPricing_ReturnsPricesAsOfDateFromConfiguration()
    {
        var response = await _factory.CreateAuthenticatedClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("asOf").GetString().Should().Be("2026-11-09");
    }

    [Fact]
    public async Task GetPricing_WrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
