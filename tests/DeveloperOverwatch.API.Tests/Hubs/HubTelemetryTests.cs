namespace DeveloperOverwatch.API.Tests.Hubs;

using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>The SignalR client puts the user's token in the hub URL's query string. With Application Insights
/// on, every request becomes a span, so the span must not carry that token. Azure Monitor's package floats on
/// 1.*, so this pins the behaviour of whatever version is resolved rather than trusting a changelog.</summary>
public class HubTelemetryTests
{
    private const string Secret = "eyJ.secret-token-value.sig";

    [Fact]
    public async Task HubRequest_WithAzureMonitorOn_SpanCarriesNoAccessTokenValue()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var factory = new AzureMonitorFactory();
        var client = factory.CreateClient();

        var controlTrace = await Send(client, "/api/pricing?probe=visible");
        var hubTrace = await Send(client, $"/hubs/overwatch?id=probe&access_token={Secret}");
        // /api refuses a query token, but a client that sends one anyway must not leak it either.
        var apiTrace = await Send(client, $"/api/sessions?access_token={Secret}");

        // Control: the instrument does record query strings, so an absent secret below is a real redaction,
        // not a span that never had a query on it.
        var control = await SpanFor(stopped, controlTrace);
        (control.GetTagItem("url.query") as string).Should().Be("?probe=visible");

        var hub = await SpanFor(stopped, hubTrace);
        (hub.GetTagItem("url.query") as string).Should().Be("?id=probe&access_token=Redacted",
            "only the token is removed; the connection id still helps diagnose a failed connect");
        RecordedValues(hub).Should().NotContain(value => value.Contains(Secret));
        RecordedValues(await SpanFor(stopped, apiTrace)).Should().NotContain(value => value.Contains(Secret));
    }

    private static IEnumerable<string> RecordedValues(Activity span) =>
        span.TagObjects.Select(t => t.Value?.ToString() ?? "").Append(span.DisplayName);

    private static async Task<ActivityTraceId> Send(HttpClient client, string url)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        using var _ = await client.SendAsync(request);
        return traceId;
    }

    // The server span stops after the response is handed back, so give it a moment to arrive.
    private static async Task<Activity> SpanFor(ConcurrentBag<Activity> stopped, ActivityTraceId traceId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var span = stopped.FirstOrDefault(a => a.TraceId == traceId && a.Kind == ActivityKind.Server);
            if (span is not null)
                return span;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"No server span recorded for trace {traceId}");
    }

    private sealed class AzureMonitorFactory : OverwatchWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // A syntactically valid connection string to a closed local port: Program.cs wires the real
            // Azure Monitor pipeline, and nothing it tries to send leaves the machine.
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/");
            builder.ConfigureTestServices(services => services.Configure<AzureMonitorOptions>(options =>
            {
                options.DisableOfflineStorage = true;
                options.EnableLiveMetrics = false;
            }));
        }
    }
}
