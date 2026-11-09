namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class AuthControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public AuthControllerTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCurrentUser_Authenticated_ReturnsAgentClaims()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("agent");
        root.GetProperty("name").GetString().Should().Be("overwatch-agent");
        root.GetProperty("authMethod").GetString().Should().Be("api_key");
        root.GetProperty("roles").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("Agent");
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

    [Fact]
    public async Task GetCurrentUser_InvalidApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
