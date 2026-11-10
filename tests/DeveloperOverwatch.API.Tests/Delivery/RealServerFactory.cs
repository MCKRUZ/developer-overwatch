namespace DeveloperOverwatch.API.Tests.Delivery;

using DeveloperOverwatch.API.Tests.Controllers;
using Microsoft.AspNetCore.Hosting;

/// <summary>The real API over in-memory SQLite, with the global rate limit lifted: one agent run sends
/// several hundred requests in a few seconds, far past the production limit of 100 a minute.</summary>
public sealed class RealServerFactory : OverwatchWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:Default:PermitLimit", "100000");
    }
}
