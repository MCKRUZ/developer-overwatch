namespace DeveloperOverwatch.API.Tests.Hubs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DeveloperOverwatch.API.Tests.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

/// <summary>Rule 1: the usage page refreshes within seconds of new usage. A real SignalR client connects through
/// the test server, so this checks the event name and the single-object payload the dashboard listens for.</summary>
public class UsageUpdatedTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly OverwatchWebApplicationFactory _factory;

    public UsageUpdatedTests(OverwatchWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task IngestEvents_OnlyTheBatchWithTurns_BroadcastsUsageUpdated()
    {
        var received = Channel.CreateUnbounded<JsonElement>();
        await using var connection = Connect();
        connection.On<JsonElement>("UsageUpdated", notification => received.Writer.TryWrite(notification));
        await connection.StartAsync();

        var withoutTurns = await StartSession("no-turns@team.dev");
        var withTurns = await StartSession("turns@team.dev");
        await PostEvents(withoutTurns, turns: []);
        await PostEvents(withTurns, turns: [Turn()]);

        // The batch without turns was posted first, so if it broadcast, its message would arrive first.
        using var timeout = new CancellationTokenSource(Wait);
        var notification = await received.Reader.ReadAsync(timeout.Token);
        notification.GetProperty("sessionId").GetString().Should().Be(withTurns);
        notification.GetProperty("developerEmail").GetString().Should().Be("turns@team.dev");
        notification.GetProperty("at").GetString().Should().EndWith("Z", "the dashboard reads it as UTC");
        DateTimeOffset.Parse(notification.GetProperty("at").GetString()!)
            .Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    private HubConnection Connect()
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/overwatch"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private async Task<string> StartSession(string developer)
    {
        var sessionId = Guid.NewGuid().ToString();
        var response = await _factory.CreateAuthenticatedClient().PostAsJsonAsync("/api/sessions/start", new
        {
            SessionId = sessionId, DeveloperEmail = developer, RepoPath = "/src/meter", StartedAt = DateTime.UtcNow,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return sessionId;
    }

    private async Task PostEvents(string sessionId, object[] turns)
    {
        var response = await _factory.CreateAuthenticatedClient().PostAsJsonAsync($"/api/sessions/{sessionId}/events", new
        {
            SessionId = sessionId,
            ConversationTurns = turns,
            ToolExecutions = new[]
            {
                new { Id = Guid.NewGuid().ToString(), ToolName = "Read", DurationMs = 5, Timestamp = DateTime.UtcNow },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static object Turn() => new
    {
        Id = Guid.NewGuid().ToString(), TurnIndex = 0, Role = "assistant", OutputTokens = 12,
        Model = "claude-sonnet", Timestamp = DateTime.UtcNow,
    };
}
