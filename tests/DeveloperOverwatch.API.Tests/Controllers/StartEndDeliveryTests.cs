namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.API.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

/// <summary>Start and end are deduplicated by the Agent's event id. A redelivery changes nothing, so
/// it must not tell the dashboard the session started or ended again, and an event id that could
/// never identify anything is refused.</summary>
public class StartEndDeliveryTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly IOverwatchHubClient _groupClient = Substitute.For<IOverwatchHubClient>();
    private readonly IOverwatchHubClient _allClient = Substitute.For<IOverwatchHubClient>();
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public StartEndDeliveryTests(OverwatchWebApplicationFactory factory)
    {
        var hubClients = Substitute.For<IHubClients<IOverwatchHubClient>>();
        hubClients.Group(Arg.Any<string>()).Returns(_groupClient);
        hubClients.All.Returns(_allClient);
        var hub = Substitute.For<IHubContext<OverwatchHub, IOverwatchHubClient>>();
        hub.Clients.Returns(hubClients);

        _client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IHubContext<OverwatchHub, IOverwatchHubClient>)).ToList())
                services.Remove(d);
            services.AddSingleton(hub);
        })).CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);
    }

    [Fact]
    public async Task Start_Duplicate_IsNotBroadcast()
    {
        var sessionId = NewId();
        var start = StartBody(sessionId, NewId());

        await PostOk("/api/sessions/start", start);
        await PostOk("/api/sessions/start", start);

        await _allClient.Received(1).SessionStarted(sessionId, Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task End_Duplicate_IsNotBroadcast()
    {
        var sessionId = NewId();
        await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));
        var end = new { sessionId, eventId = NewId(), endedAt = _past };

        await PostOk($"/api/sessions/{sessionId}/end", end);
        await PostOk($"/api/sessions/{sessionId}/end", end);

        await _allClient.Received(1).SessionEnded(sessionId);
        await _groupClient.Received(1).SessionEnded(sessionId);
    }

    // An event id dedupes within its own session only. One recorded for session A must not make
    // session B's start or end look already processed, or B would never be created or closed.
    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    public async Task StartOrEnd_EventIdAlreadyUsedByAnotherSession_IsAcceptedThenDuplicate(string action)
    {
        var (a, b, eventId) = (NewId(), NewId(), NewId());
        await PostOk("/api/sessions/start", StartBody(a, action == "start" ? eventId : NewId()));
        if (action == "end")
        {
            await PostOk($"/api/sessions/{a}/end", new { sessionId = a, eventId, endedAt = _past });
            await PostOk("/api/sessions/start", StartBody(b, NewId()));
        }
        var (path, body) = action == "start"
            ? ("/api/sessions/start", StartBody(b, eventId))
            : ($"/api/sessions/{b}/end", (object)new { sessionId = b, eventId, endedAt = _past });

        var first = await PostOutcome(path, body);
        var second = await PostOutcome(path, body);

        first.Should().Be("accepted");
        second.Should().Be("duplicate");
    }

    [Theory]
    [InlineData("start", "")]
    [InlineData("start", "   ")]
    [InlineData("start", "x65")]
    [InlineData("end", "")]
    [InlineData("end", "   ")]
    [InlineData("end", "x65")]
    public async Task StartOrEnd_EventIdBlankOrTooLong_Returns400(string action, string eventId)
    {
        if (eventId == "x65")
            eventId = new string('x', 65);
        var sessionId = NewId();
        if (action == "end")
            await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));

        var response = action == "start"
            ? await _client.PostAsJsonAsync("/api/sessions/start", StartBody(sessionId, eventId))
            : await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", new { sessionId, eventId, endedAt = _past });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private object StartBody(string sessionId, string? eventId) =>
        new { sessionId, eventId, developerEmail = "startend@team.dev", startedAt = _past };

    private async Task PostOk(string path, object body)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<string?> PostOutcome(string path, object body)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("outcome").GetString();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
