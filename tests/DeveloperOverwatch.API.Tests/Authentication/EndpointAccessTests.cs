namespace DeveloperOverwatch.API.Tests.Authentication;

using System.Net;
using System.Text.RegularExpressions;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>One mapped route, as a request the test can send. Agent routes are the ones only a developer
/// machine's key may call; budget-edit routes need the Lead role with sign-in on; every other route belongs to the dashboard, the webhook or the health probe.</summary>
public sealed record MappedRoute(
    string Method, string Url, bool IsAgent, bool IsAnonymous, bool IsBudgetEdit, string Name)
{
    public override string ToString() => $"{Method} {Url}";

    private static readonly Regex Parameter = new(@"\{(?<name>[^}:=?]+)(?<constraint>:[^}=?]+)?[^}]*\}", RegexOptions.Compiled);

    /// <summary>Every route the running app maps, read from the router rather than from the controllers, so a new
    /// controller or hub cannot be missed.</summary>
    public static IReadOnlyList<MappedRoute> All(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                    ?? ["GET"];
                var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
                var isAgent = authorize.Any(a => a.Policy == AuthPolicies.Agent);
                var isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var isBudgetEdit = authorize.Any(a => a.Policy == AuthPolicies.BudgetEdit);
                var url = "/" + Parameter.Replace(endpoint.RoutePattern.RawText!.TrimStart('/'),
                    m => Filler(m.Groups["constraint"].Value));
                return methods.Select(method => new MappedRoute(
                    method, url, isAgent, isAnonymous, isBudgetEdit, endpoint.DisplayName ?? url));
            })
            .ToList();

    // A value the route constraint accepts, so the request reaches authorization instead of a 404 from routing.
    private static string Filler(string constraint) =>
        constraint.Contains("guid") ? "3f1c2b7e-9a4d-4e2f-8b6a-0c5d1e7f9a20"
        : constraint.Contains("int") ? "1"
        : "x";
}

/// <summary>The agent key is on every developer machine. With OIDC on, it must open the agent routes and nothing
/// else: before this, the older dashboard controllers took it as a sign-in, so any developer could read the team's
/// analytics, resolve violations or review PRs with it.</summary>
public class EndpointAccessOidcTests : IClassFixture<OidcWebApplicationFactory>
{
    private readonly OidcWebApplicationFactory _factory;

    public EndpointAccessOidcTests(OidcWebApplicationFactory factory) => _factory = factory;

    private IReadOnlyList<MappedRoute> Routes() => MappedRoute.All(_factory.Services);

    [Fact]
    public void Routes_AreDiscovered_IncludingAgentAndDashboardRoutes()
    {
        var routes = Routes();

        routes.Count(r => r.IsAgent).Should().Be(3, "start, events and end are the agent's only routes");
        routes.Should().Contain(r => r.Url == "/api/analytics/team");
        routes.Should().Contain(r => r.Url.StartsWith("/hubs/overwatch"));
    }

    // Leaving [Authorize] off a new controller would make it public, since no fallback policy is set.
    [Fact]
    public void EveryRoute_ExceptHealth_DeclaresWhoMayCallIt()
    {
        var undeclared = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText != "/health")
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null
                && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.DisplayName);

        undeclared.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryNonAgentRoute_ValidAgentKeyOnly_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);

        var opened = new List<string>();
        foreach (var route in Routes().Where(r => !r.IsAgent && !r.IsAnonymous && r.Url != "/health"))
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(route.Method), route.Url));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                opened.Add($"{route} -> {(int)response.StatusCode}");
        }

        opened.Should().BeEmpty("the agent key must not sign anyone in to the dashboard");
    }

    // Control: a signed-in dashboard user is not refused on those routes, so the 401s above are the key being refused.
    // Budget-edit routes need the Lead role on top; the next test is their control.
    [Fact]
    public async Task EveryNonAgentRoute_BearerToken_IsNot401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", OidcWebApplicationFactory.MintToken("endpoint-access-user"));

        var refused = new List<string>();
        foreach (var route in Routes().Where(r => !r.IsAgent && !r.IsAnonymous && !r.IsBudgetEdit && r.Url != "/health"))
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(route.Method), route.Url));
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                refused.Add($"{route} -> {(int)response.StatusCode}");
        }

        refused.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryBudgetEditRoute_LeadBearerToken_IsNotRefused()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", OidcWebApplicationFactory.MintToken("endpoint-access-lead", AppRoles.Lead));
        var editRoutes = Routes().Where(r => r.IsBudgetEdit).ToList();

        editRoutes.Should().HaveCount(6, "budgets and developer plans each have create, update and delete");
        var refused = new List<string>();
        foreach (var route in editRoutes)
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(route.Method), route.Url));
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                refused.Add($"{route} -> {(int)response.StatusCode}");
        }

        refused.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryAgentRoute_BearerTokenOnly_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", OidcWebApplicationFactory.MintToken("endpoint-access-user"));

        foreach (var route in Routes().Where(r => r.IsAgent))
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(route.Method), route.Url));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, route.ToString());
        }
    }
}

/// <summary>With OIDC off the dashboard is anonymous, and the agent routes sit on a controller whose other routes
/// are dashboard routes. Anonymous access to the dashboard must not leak into the agent routes.</summary>
public class EndpointAccessAnonymousTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;

    public EndpointAccessAnonymousTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EveryAgentRoute_OidcOffAndNoKey_Returns401()
    {
        var client = _factory.CreateClient();
        var agentRoutes = MappedRoute.All(_factory.Services).Where(r => r.IsAgent).ToList();

        agentRoutes.Should().HaveCount(3);
        foreach (var route in agentRoutes)
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(route.Method), route.Url));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, route.ToString());
        }
    }
}
