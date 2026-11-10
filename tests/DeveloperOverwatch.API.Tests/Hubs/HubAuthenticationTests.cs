namespace DeveloperOverwatch.API.Tests.Hubs;

using System.Net;
using System.Net.Http.Headers;
using DeveloperOverwatch.API.Tests.Authentication;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Xunit;

/// <summary>The hub broadcasts per-developer usage, so with OIDC on it must require a signed-in user. Browsers
/// cannot set headers on a WebSocket, so the SignalR client sends the token as <c>?access_token=</c>; that
/// channel must open the hub and nothing else, since query strings end up in logs and browser history.</summary>
public class HubAuthenticationTests : IClassFixture<OidcWebApplicationFactory>
{
    private const string Negotiate = "/hubs/overwatch/negotiate?negotiateVersion=1";
    private readonly OidcWebApplicationFactory _factory;

    public HubAuthenticationTests(OidcWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Negotiate_OidcOnAndNoToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(Negotiate, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Negotiate_OidcOnAndBearerHeader_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OidcWebApplicationFactory.MintToken("hub-header-user"));

        var response = await client.PostAsync(Negotiate, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Negotiate_OidcOnAndAccessTokenQuery_Returns200()
    {
        var token = OidcWebApplicationFactory.MintToken("hub-query-user");

        var response = await _factory.CreateClient().PostAsync($"{Negotiate}&access_token={token}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiRoute_AccessTokenQuery_DoesNotAuthenticate()
    {
        var token = OidcWebApplicationFactory.MintToken("api-query-user");

        var response = await _factory.CreateClient().GetAsync($"/api/sessions?access_token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Control for the test above: the same kind of token in a header does reach /api/sessions, so the 401
    // there is the query channel being refused, not a bad token.
    [Fact]
    public async Task ApiRoute_SameTokenAsBearerHeader_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OidcWebApplicationFactory.MintToken("api-query-user"));

        var response = await client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Without OIDC the dashboard runs anonymously; requiring sign-in on the hub must not lock it out.</summary>
public class HubAnonymousTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public HubAnonymousTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Negotiate_NoOidcConfigured_Returns200()
    {
        var response = await _factory.CreateClient().PostAsync("/hubs/overwatch/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
