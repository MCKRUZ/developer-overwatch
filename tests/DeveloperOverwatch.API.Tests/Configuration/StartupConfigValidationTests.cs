namespace DeveloperOverwatch.API.Tests.Configuration;

using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// The price table and context-window table are built lazily by singleton factories, and the container
/// does not cache a factory exception. Without an eager resolve at startup, bad config would let the app
/// boot healthy and then fail every ingest request; these tests pin that startup refuses instead.
/// </summary>
public class StartupConfigValidationTests
{
    [Theory]
    [InlineData("TokenPricing:AsOf")]
    [InlineData("ModelContextWindows:AsOf")]
    public void Startup_TableAsOfUnset_FailsWithOriginalException(string asOfKey)
    {
        using var factory = new BadConfigFactory(asOfKey);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage($"{asOfKey} must be set*");
    }

    private sealed class BadConfigFactory(string asOfKey) : OverwatchWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [asOfKey] = DateOnly.MinValue.ToString("yyyy-MM-dd")
                }));
        }
    }
}
