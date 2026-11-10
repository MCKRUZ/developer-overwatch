namespace DeveloperOverwatch.API.Tests.Hubs;

using System.Collections.Concurrent;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>The SignalR client puts the user's token in the hub URL's query string, and with Application Insights
/// on every log record is exported. Our own middleware logs only the path, but ASP.NET Core's hosting log
/// ("Request starting ...") includes the full query. HostingLogFloor holds that category at Warning for every
/// provider in code, so lowering the levels in configuration cannot put the token in the logs.</summary>
public class HubLoggingTests
{
    private const string Secret = "eyJ.log-secret-token.sig";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Hosting = "Microsoft.AspNetCore.Hosting.Diagnostics";
    private static readonly string[] HubUrls =
    [
        $"/hubs/overwatch/negotiate?negotiateVersion=1&access_token={Secret}",
        $"/hubs/overwatch?id=probe&access_token={Secret}",
    ];

    public static TheoryData<string[]> LoweredLevels => new()
    {
        { [$"Logging:LogLevel:{AspNetCore}=Information"] },
        { [$"Logging:LogLevel:{AspNetCore}=Information", $"Logging:LogLevel:{Hosting}=Information"] },
        // A provider-specific rule beats every provider-less one, whatever its category.
        { [$"Logging:Capture:LogLevel:{AspNetCore}=Information", $"Logging:Capture:LogLevel:{Hosting}=Information"] },
        { ["Logging:LogLevel:Default=Trace", $"Logging:LogLevel:{AspNetCore}=Trace"] },
    };

    [Fact]
    public async Task HubRequests_WithShippedLogLevels_NoLogRecordCarriesTheToken()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new LoggingFactory(logs, []);

        await SendHubRequests(factory);

        logs.Messages.Should().NotBeEmpty("the capture must be live for an absent token to mean anything");
        logs.Messages.Should().NotContain(m => m.Contains(Secret));
    }

    [Theory]
    [MemberData(nameof(LoweredLevels))]
    public async Task HubRequests_WithAspNetCoreLevelsLowered_NoLogRecordCarriesTheToken(string[] settings)
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new LoggingFactory(logs, settings);

        await SendHubRequests(factory);

        // Control: the lowered level is live, so the capture would see an Information record from ASP.NET Core.
        logs.Messages.Should().Contain(m => m.StartsWith("Microsoft.AspNetCore.Routing") && m.Contains("Executing endpoint"));
        logs.Messages.Should().NotContain(m => m.Contains(Secret));
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

    private sealed class LoggingFactory(CapturingLoggerProvider logs, string[] settings)
        : OverwatchWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var setting in settings)
            {
                var (key, value) = (setting[..setting.IndexOf('=')], setting[(setting.IndexOf('=') + 1)..]);
                builder.UseSetting(key, value);
            }
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }
    }

    [ProviderAlias("Capture")]
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
