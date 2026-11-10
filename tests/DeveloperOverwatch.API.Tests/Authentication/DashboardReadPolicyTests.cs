namespace DeveloperOverwatch.API.Tests.Authentication;

using System.Net;
using System.Net.Http.Headers;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Xunit;

/// <summary>The hub and the usage meter show every developer's usage. With OIDC on they must take a signed-in
/// user only: an agent API key is handed to every developer machine, so accepting it here would let any one
/// of them read the whole team's usage without signing in.</summary>
public class DashboardReadPolicyTests : IClassFixture<OidcWebApplicationFactory>
{
    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "/hubs/overwatch/negotiate?negotiateVersion=1" },
        { "GET", "/api/usage/summary?from=2026-01-01&to=2026-01-07&granularity=day&timeZone=UTC" },
        { "GET", "/api/usage/running" },
    };

    private readonly OidcWebApplicationFactory _factory;

    public DashboardReadPolicyTests(OidcWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_OidcOnAndValidAgentApiKey_Returns401(string method, string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Control: a signed-in user reaches the same routes, so the 401 above is the key being refused.
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_OidcOnAndBearerToken_Returns200(string method, string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OidcWebApplicationFactory.MintToken("dashboard-read-user"));

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Without OIDC the dashboard runs anonymously, so the same routes must stay open with no headers.</summary>
public class DashboardReadAnonymousTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public DashboardReadAnonymousTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [MemberData(nameof(DashboardReadPolicyTests.Routes), MemberType = typeof(DashboardReadPolicyTests))]
    public async Task Route_OidcOffAndNoHeaders_Returns200(string method, string url)
    {
        var response = await _factory.CreateClient()
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
