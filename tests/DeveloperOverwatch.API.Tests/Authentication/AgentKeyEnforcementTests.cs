namespace DeveloperOverwatch.API.Tests.Authentication;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>The real API run as Production, with the configured agent keys replaced by <see cref="Keys"/>.
/// A blank entry is how a missing secret arrives (an env var set to nothing), so it must count as no key.</summary>
public abstract class ProductionKeysFactory : OverwatchWebApplicationFactory
{
    protected abstract string[] Keys { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration(config =>
        {
            var keys = new Dictionary<string, string?> { ["ApiKeys:Keys:0"] = "" };
            for (var i = 0; i < Keys.Length; i++)
                keys[$"ApiKeys:Keys:{i}"] = Keys[i];
            config.AddInMemoryCollection(keys);
        });
    }
}

public sealed class ProductionNoKeysFactory : ProductionKeysFactory
{
    protected override string[] Keys => ["  "];
}

public sealed class ProductionTwoKeysFactory : ProductionKeysFactory
{
    public const string CurrentKey = "current-agent-key-0123456789abcdef";
    public const string NextKey = "next-agent-key-0123456789abcdef0123";

    // The next key is stored the way a pasted or file-loaded secret often arrives: with a trailing newline.
    protected override string[] Keys => [CurrentKey, NextKey + "\n"];
}

/// <summary>Development with no keys configured: the only place any key is still accepted.</summary>
public sealed class DevelopmentNoKeysFactory : OverwatchWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeys:Keys:0"] = "" }));
    }
}

/// <summary>Every developer machine ships usage with the agent key. Outside Development the server must accept only
/// the keys it was given: before this, an empty key list meant any non-empty key got in, and production set none.</summary>
public class AgentKeyEnforcementTests
{
    private static object StartBody() => new
    {
        SessionId = Guid.NewGuid().ToString(),
        DeveloperEmail = "key-enforcement@team.dev",
        RepoPath = "C:/repos/test",
        StartedAt = DateTime.UtcNow,
    };

    private static async Task<HttpStatusCode> StartWithKey(OverwatchWebApplicationFactory factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return (await client.PostAsJsonAsync("/api/sessions/start", StartBody())).StatusCode;
    }

    // A production server with no key would refuse every agent while looking healthy; stopping startup makes the
    // missing secret visible at deploy time instead.
    [Fact]
    public void Startup_ProductionWithNoKeys_Throws()
    {
        using var factory = new ProductionNoKeysFactory();

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*ApiKeys:Keys*");
    }

    [Fact]
    public async Task Start_ProductionAndUnknownKey_Returns401()
    {
        using var factory = new ProductionTwoKeysFactory();

        (await StartWithKey(factory, "any-other-non-empty-key")).Should().Be(HttpStatusCode.Unauthorized);
    }

    // A key that shares a prefix with a real one, or differs only in case, is still a different key.
    [Theory]
    [InlineData("current-agent-key")]
    [InlineData("CURRENT-AGENT-KEY-0123456789ABCDEF")]
    [InlineData("current-agent-key-0123456789abcdef0")]
    public async Task Start_ProductionAndNearMissKey_Returns401(string key)
    {
        using var factory = new ProductionTwoKeysFactory();

        (await StartWithKey(factory, key)).Should().Be(HttpStatusCode.Unauthorized);
    }

    // Two keys at once is how a key is rotated without cutting off agents that still hold the old one. The next key
    // is configured with a trailing newline and matches the trimmed key the installer saves.
    [Theory]
    [InlineData(ProductionTwoKeysFactory.CurrentKey)]
    [InlineData(ProductionTwoKeysFactory.NextKey)]
    public async Task Start_ProductionAndConfiguredKey_Returns200(string key)
    {
        using var factory = new ProductionTwoKeysFactory();

        (await StartWithKey(factory, key)).Should().Be(HttpStatusCode.OK);
    }

    // Control: local development keeps working with no key set up, so the refusals above are the environment rule.
    [Fact]
    public async Task Start_DevelopmentWithNoKeysAndAnyKey_Returns200()
    {
        using var factory = new DevelopmentNoKeysFactory();

        (await StartWithKey(factory, "whatever-the-developer-typed")).Should().Be(HttpStatusCode.OK);
    }
}
