namespace DeveloperOverwatch.API.Tests.Hubs;

using System.Collections.Concurrent;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>The SignalR client puts the user's token in the hub URL's query string, and with Application Insights
/// on every log record is exported. Our own middleware logs only the path, but ASP.NET Core's hosting log
/// ("Request starting ...") includes the full query. It is written at Information, so the shipped
/// <c>Microsoft.AspNetCore: Warning</c> level in appsettings.json is what keeps the token out of the logs.</summary>
public class HubLoggingTests
{
    private const string Secret = "eyJ.log-secret-token.sig";
    private static readonly string[] HubUrls =
    [
        $"/hubs/overwatch/negotiate?negotiateVersion=1&access_token={Secret}",
        $"/hubs/overwatch?id=probe&access_token={Secret}",
    ];

    [Fact]
    public async Task HubRequests_WithShippedLogLevels_NoLogRecordCarriesTheToken()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new LoggingFactory(logs, aspNetCoreLevel: null);

        await SendHubRequests(factory);

        logs.Messages.Should().NotBeEmpty("the capture must be live for an absent token to mean anything");
        logs.Messages.Should().NotContain(m => m.Contains(Secret));
    }

    [Fact]
    public async Task HubRequests_WithAspNetCoreAtInformation_HostingLogCarriesTheToken()
    {
        // Control: proves the capture would see the token if it were logged, and pins why the shipped
        // Warning level for Microsoft.AspNetCore must not be lowered in production.
        var logs = new CapturingLoggerProvider();
        using var factory = new LoggingFactory(logs, aspNetCoreLevel: "Information");

        await SendHubRequests(factory);

        logs.Messages.Should().Contain(m => m.Contains(Secret));
    }

    private static async Task SendHubRequests(OverwatchWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        foreach (var url in HubUrls)
        {
            using var request = new HttpRequestMessage(url.Contains("negotiate") ? HttpMethod.Post : HttpMethod.Get, url);
            using var _ = await client.SendAsync(request);
        }
        // Anything logged after the response is written (request finished, connection cleanup).
        await Task.Delay(200);
    }

    private sealed class LoggingFactory(CapturingLoggerProvider logs, string? aspNetCoreLevel)
        : OverwatchWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (aspNetCoreLevel is not null)
                builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", aspNetCoreLevel);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => [.. _messages];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
