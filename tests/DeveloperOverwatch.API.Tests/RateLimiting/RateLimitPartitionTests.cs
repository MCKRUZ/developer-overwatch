namespace DeveloperOverwatch.API.Tests.RateLimiting;

using System.Net;
using DeveloperOverwatch.API.Tests.Authentication;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>Sends requests straight into the test server so each one can carry its own client IP.</summary>
internal static class RateLimitProbe
{
    public const int Limit = 3;

    public static async Task<int> Get(
        TestServer server, string ip, string? bearer = null, string? apiKey = null)
    {
        var context = await server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/api/pricing";
            c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            if (bearer is not null)
                c.Request.Headers.Authorization = $"Bearer {bearer}";
            if (apiKey is not null)
                c.Request.Headers["X-Api-Key"] = apiKey;
        });
        return context.Response.StatusCode;
    }

    public static async Task UseUpLimit(TestServer server, string ip, string? bearer = null, string? apiKey = null)
    {
        for (var i = 0; i < Limit; i++)
            (await Get(server, ip, bearer, apiKey)).Should().Be(StatusCodes.Status200OK);
    }
}

public sealed class OidcRateLimitFactory : OidcWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:Default:PermitLimit", RateLimitProbe.Limit.ToString());
    }
}

public sealed class AnonymousRateLimitFactory : OverwatchWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:Default:PermitLimit", RateLimitProbe.Limit.ToString());
    }
}

/// <summary>Development with no keys configured: the API key handler then accepts any non-empty key.</summary>
public sealed class OpenApiKeyRateLimitFactory : OverwatchWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("RateLimiting:Default:PermitLimit", RateLimitProbe.Limit.ToString());
        builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeys:Keys:0"] = "" }));
    }
}

/// <summary>In Development with no keys configured any key authenticates, so a bucket per key would let a caller dodge the
/// limit by sending a new key on every request. Agents stay on their machine's IP bucket.</summary>
public class OpenApiKeyRateLimitTests : IClassFixture<OpenApiKeyRateLimitFactory>
{
    private readonly TestServer _server;

    public OpenApiKeyRateLimitTests(OpenApiKeyRateLimitFactory factory) => _server = factory.Server;

    [Fact]
    public async Task RotatingApiKeysFromOneIp_StillGets429()
    {
        const string ip = "10.4.0.1";
        for (var i = 0; i < RateLimitProbe.Limit; i++)
            (await RateLimitProbe.Get(_server, ip, apiKey: Guid.NewGuid().ToString()))
                .Should().Be(StatusCodes.Status200OK);

        var status = await RateLimitProbe.Get(_server, ip, apiKey: Guid.NewGuid().ToString());

        status.Should().Be(StatusCodes.Status429TooManyRequests);
    }
}

/// <summary>Behind nginx every dashboard user arrives from one IP, so an IP bucket makes one busy user
/// throttle everyone. Signed-in users get a bucket each, keyed on their verified subject.</summary>
public class SignedInRateLimitTests : IClassFixture<OidcRateLimitFactory>
{
    private const string SharedProxyIp = "10.0.0.1";
    private readonly TestServer _server;

    public SignedInRateLimitTests(OidcRateLimitFactory factory) => _server = factory.Server;

    // Both tokens carry the same display name, so this also proves the bucket is keyed on the subject.
    [Fact]
    public async Task TwoSignedInUsersBehindOneProxy_DoNotShareABucket()
    {
        await RateLimitProbe.UseUpLimit(_server, SharedProxyIp, OidcWebApplicationFactory.MintToken("user-a"));

        var status = await RateLimitProbe.Get(_server, SharedProxyIp, OidcWebApplicationFactory.MintToken("user-b"));

        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task OneSignedInUserOverTheLimit_Gets429_EvenFromAnotherIp()
    {
        var token = OidcWebApplicationFactory.MintToken("user-c");
        await RateLimitProbe.UseUpLimit(_server, "10.0.0.2", token);

        var status = await RateLimitProbe.Get(_server, "10.0.0.3", token);

        status.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    // Only a validated token earns its own bucket. Otherwise anyone could mint a fresh bucket per request
    // by sending a different garbage token each time.
    [Fact]
    public async Task InvalidTokens_FallBackToTheIpBucket()
    {
        const string ip = "10.0.0.4";
        for (var i = 0; i < RateLimitProbe.Limit; i++)
            await RateLimitProbe.Get(_server, ip, $"not-a-jwt-{i}");

        var status = await RateLimitProbe.Get(_server, ip, "not-a-jwt-last");

        status.Should().Be(StatusCodes.Status429TooManyRequests);
    }
}

/// <summary>The anonymous dashboard and the agents' API key each give every caller the same identity
/// ("anonymous", "agent"). Keying on that identity would pool the whole team, or every agent, into one bucket.</summary>
public class SharedIdentityRateLimitTests : IClassFixture<AnonymousRateLimitFactory>
{
    private readonly TestServer _server;

    public SharedIdentityRateLimitTests(AnonymousRateLimitFactory factory) => _server = factory.Server;

    [Fact]
    public async Task AnonymousCallersOnDifferentIps_DoNotShareABucket()
    {
        await RateLimitProbe.UseUpLimit(_server, "10.1.0.1");

        var status = await RateLimitProbe.Get(_server, "10.1.0.2");

        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task AgentsWithTheSameKeyOnDifferentMachines_DoNotShareABucket()
    {
        const string key = OverwatchWebApplicationFactory.TestApiKey;
        await RateLimitProbe.UseUpLimit(_server, "10.2.0.1", apiKey: key);

        var status = await RateLimitProbe.Get(_server, "10.2.0.2", apiKey: key);

        status.Should().Be(StatusCodes.Status200OK);
    }

    // The dashboard and the agent on a developer's machine share its IP. A dashboard over its limit (a busy
    // page, or anyone browsing from that address) must not throttle the agent's ingest, or telemetry is lost.
    [Fact]
    public async Task DashboardOverTheLimit_DoesNotThrottleTheAgentOnTheSameIp()
    {
        const string ip = "10.5.0.1";
        await RateLimitProbe.UseUpLimit(_server, ip);

        var status = await RateLimitProbe.Get(_server, ip, apiKey: OverwatchWebApplicationFactory.TestApiKey);

        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task OneAgentOverTheLimit_Gets429()
    {
        const string key = OverwatchWebApplicationFactory.TestApiKey;
        await RateLimitProbe.UseUpLimit(_server, "10.3.0.1", apiKey: key);

        var status = await RateLimitProbe.Get(_server, "10.3.0.1", apiKey: key);

        status.Should().Be(StatusCodes.Status429TooManyRequests);
    }
}
