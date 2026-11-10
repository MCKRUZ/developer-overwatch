namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class UsageControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public UsageControllerTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRunning_SessionJustStarted_IsListedWithItsProject()
    {
        var sessionId = Guid.NewGuid().ToString();
        var start = await _factory.CreateAuthenticatedClient().PostAsJsonAsync("/api/sessions/start", new
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            RepoPath = "/src/meter",
            StartedAt = DateTime.UtcNow
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _factory.CreateClient().GetAsync("/api/usage/running");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = body.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == sessionId);
        row.GetProperty("project").GetString().Should().Be("/src/meter");
        row.GetProperty("burnTokensPerMinute").GetDouble().Should().Be(0);
    }

    // The usage page is a dashboard read. Without OIDC configured the dashboard runs anonymously, so the
    // endpoint must use the default (Smart) scheme, not the JwtBearer-only Dashboard policy.
    [Fact]
    public async Task GetRunning_NoOidcConfiguredAndNoCredentials_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/api/usage/running");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRunning_WrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/usage/running");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
