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

    // Each step that must stay silent is followed by one that must broadcast. Messages arrive in order, so if a
    // silent step broadcast, its message would be read before the sentinel's and the session id would differ.
    [Fact]
    public async Task IngestEvents_BroadcastsOnlyWhenTurnsOrLiveStateWereStored()
    {
        var received = Channel.CreateUnbounded<JsonElement>();
        await using var connection = Connect();
        connection.On<JsonElement>("UsageUpdated", notification => received.Writer.TryWrite(notification));
        await connection.StartAsync();

        var empty = await StartSession("empty@team.dev");
        var withTurns = await StartSession("turns@team.dev");
        var toolsOnly = await StartSession("tools@team.dev");
        var activityOnly = await StartSession("activity@team.dev");
        var failureOnly = await StartSession("failure@team.dev");
        object[] turns = [Turn()];

        await PostEvents(withTurns, new { ConversationTurns = turns });
        (await Next(received)).GetProperty("sessionId").GetString().Should().Be(withTurns);

        await PostEvents(empty, new { });
        await PostEvents(withTurns, new { ConversationTurns = turns });
        await PostEvents(toolsOnly, new { ToolExecutions = new[] { Tool() } });
        await PostEvents(activityOnly, new { ActivityEvents = new[] { new { State = "Active", At = DateTime.UtcNow } } });
        var activity = await Next(received);
        activity.GetProperty("sessionId").GetString().Should().Be(
            activityOnly, "an empty batch, resent duplicate turns and tool executions alone change nothing shown");

        await PostEvents(failureOnly, new { ToolFailures = new[] { Failure() } });
        (await Next(received)).GetProperty("sessionId").GetString().Should().Be(failureOnly);
    }

    [Fact]
    public async Task UsageUpdated_Payload_IsSessionIdAndUtcTimeOnly()
    {
        var received = Channel.CreateUnbounded<JsonElement>();
        await using var connection = Connect();
        connection.On<JsonElement>("UsageUpdated", notification => received.Writer.TryWrite(notification));
        await connection.StartAsync();

        var sessionId = await StartSession("payload@team.dev");
        await PostEvents(sessionId, new { ConversationTurns = new[] { Turn() } });

        var notification = await Next(received);
        notification.GetProperty("sessionId").GetString().Should().Be(sessionId);
        notification.TryGetProperty("developerEmail", out _)
            .Should().BeFalse("every dashboard client receives the broadcast, whoever is signed in");
        notification.GetProperty("at").GetString().Should().EndWith("Z", "the dashboard reads it as UTC");
        DateTimeOffset.Parse(notification.GetProperty("at").GetString()!)
            .Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    private static async Task<JsonElement> Next(Channel<JsonElement> received)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await received.Reader.ReadAsync(timeout.Token);
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

    // The endpoint requires the body's session id to match the route's.
    private async Task PostEvents(string sessionId, object batch)
    {
        var body = JsonSerializer.SerializeToNode(batch)!.AsObject();
        body["sessionId"] = sessionId;
        var response = await _factory.CreateAuthenticatedClient().PostAsJsonAsync($"/api/sessions/{sessionId}/events", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static object Tool() => new
    {
        Id = Guid.NewGuid().ToString(), ToolName = "Read", DurationMs = 5, Timestamp = DateTime.UtcNow,
    };

    private static object Failure() => new { Id = Guid.NewGuid().ToString(), ToolName = "Bash", At = DateTime.UtcNow };

    private static object Turn() => new
    {
        Id = Guid.NewGuid().ToString(), TurnIndex = 0, Role = "assistant", OutputTokens = 12,
        Model = "claude-sonnet", Timestamp = DateTime.UtcNow,
    };
}
