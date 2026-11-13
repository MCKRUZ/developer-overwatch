namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class AuthControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public AuthControllerTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    // The agent key is not a dashboard sign-in: with OIDC off the caller is anonymous whatever key it sends.
    // The OIDC-on case, where the key gets 401, is covered route by route in EndpointAccessOidcTests.
    [Theory]
    [InlineData(OverwatchWebApplicationFactory.TestApiKey)]
    [InlineData("wrong-key")]
    public async Task GetCurrentUser_AgentKeyNoOidcConfigured_ReturnsAnonymousNotAgent(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("authMethod").GetString().Should().Be("anonymous");
    }

    [Fact]
    public async Task GetCurrentUser_NoApiKeyNoOidcConfigured_ReturnsAnonymousIdentity()
    {
        // With no OIDC configured (the test host's default), dashboard-facing endpoints fall
        // back to an anonymous identity rather than 401 — matching README's documented
        // "no OIDC = dashboard runs without authentication" behavior.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("authMethod").GetString().Should().Be("anonymous");
    }
}
