namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The Agent delivers at-least-once and marks each item done from the outcome the server reports.
/// Every event kind must be idempotent by its key (sending it twice leaves the state sending it
/// once left), and every item must get its own outcome, so one bad item never sinks the others.
/// </summary>
public class IngestDeliveryTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public IngestDeliveryTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("turn")]
    [InlineData("toolExecution")]
    [InlineData("fileChange")]
    [InlineData("toolFailure")]
    [InlineData("activity")]
    public async Task Ingest_SameEventTwice_LeavesTheStateSendingItOnceLeft(string kind)
    {
        var sessionId = NewId();
        if (kind != "start")
            await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));

        var (path, body) = Payload(kind, sessionId);
        await PostOk(path, body);
        var once = await Snapshot(sessionId);

        await PostOk(path, body);

        (await Snapshot(sessionId)).Should().Be(once);
    }

    [Fact]
    public async Task Start_RedeliveredAfterEnd_IsDuplicateAndDoesNotReopenTheSession()
    {
        var sessionId = NewId();
        var start = StartBody(sessionId, NewId());
        await PostOk("/api/sessions/start", start);
        await PostOk($"/api/sessions/{sessionId}/end", EndBody(sessionId, NewId()));
        var ended = await Snapshot(sessionId);

        var redelivered = await PostOk("/api/sessions/start", start);

        redelivered.GetProperty("outcome").GetString().Should().Be("duplicate");
        (await Snapshot(sessionId)).Should().Be(ended);
        ended.Should().Contain("\"Status\":\"Completed\"");
    }

    [Fact]
    public async Task Start_SameEventIdTwice_ReportsAcceptedThenDuplicate()
    {
        var sessionId = NewId();
        var start = StartBody(sessionId, NewId());

        var first = await PostOk("/api/sessions/start", start);
        var second = await PostOk("/api/sessions/start", start);

        first.GetProperty("outcome").GetString().Should().Be("accepted");
        first.GetProperty("sessionId").GetString().Should().Be(sessionId);
        second.GetProperty("outcome").GetString().Should().Be("duplicate");
    }

    [Fact]
    public async Task End_SameEventIdTwice_ReportsAcceptedThenDuplicate()
    {
        var sessionId = NewId();
        await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));
        var end = EndBody(sessionId, NewId());

        var first = await PostOk($"/api/sessions/{sessionId}/end", end);
        var second = await PostOk($"/api/sessions/{sessionId}/end", end);

        first.GetProperty("outcome").GetString().Should().Be("accepted");
        first.GetProperty("sessionId").GetString().Should().Be(sessionId);
        second.GetProperty("outcome").GetString().Should().Be("duplicate");
    }

    [Fact]
    public async Task StartAndEnd_WithoutEventId_BehaveAsBefore()
    {
        var sessionId = NewId();
        (await PostOk("/api/sessions/start", StartBody(sessionId, eventId: null)))
            .GetProperty("outcome").GetString().Should().Be("accepted");
        await PostOk($"/api/sessions/{sessionId}/end", EndBody(sessionId, eventId: null));

        // An old Agent's resume start still reopens the session, as it always has.
        (await PostOk("/api/sessions/start", StartBody(sessionId, eventId: null)))
            .GetProperty("outcome").GetString().Should().Be("accepted");

        (await Snapshot(sessionId)).Should().Contain("\"Status\":\"Active\"");
    }

    [Fact]
    public async Task Events_OneInvalidTurn_RejectsOnlyThatTurnAndAResendReportsTheRestAsDuplicate()
    {
        var sessionId = NewId();
        await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));
        var (good1, bad, good2, tool, file, failure) = (NewId(), NewId(), NewId(), NewId(), NewId(), NewId());
        var batch = new
        {
            sessionId,
            conversationTurns = new object[]
            {
                Turn(good1),
                new { id = bad, role = "assistant", timestamp = _past, gitBranch = new string('b', 300) },
                Turn(good2)
            },
            toolExecutions = new[] { new { id = tool, toolName = "Bash", timestamp = _past } },
            fileChanges = new[] { new { id = file, filePath = "src/a.cs" } },
            toolFailures = new[] { new { id = failure, toolName = "Bash", at = _past } }
        };

        var first = await PostOk($"/api/sessions/{sessionId}/events", batch);

        first.GetProperty("eventsIngested").GetInt32().Should().Be(5);
        Ids(first, "accepted").Should().BeEquivalentTo(good1, good2, tool, file, failure);
        Ids(first, "duplicate").Should().BeEmpty();
        var rejected = first.GetProperty("results").GetProperty("rejected").EnumerateArray().ToList();
        rejected.Should().ContainSingle();
        rejected[0].GetProperty("id").GetString().Should().Be(bad);
        rejected[0].GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        (await Snapshot(sessionId)).Should().Contain("\"Turns\":2");

        var resent = await PostOk($"/api/sessions/{sessionId}/events", batch);

        resent.GetProperty("eventsIngested").GetInt32().Should().Be(0);
        Ids(resent, "accepted").Should().BeEmpty();
        Ids(resent, "duplicate").Should().BeEquivalentTo(good1, good2, tool, file, failure);
        resent.GetProperty("results").GetProperty("rejected").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()).Should().Equal(bad);
    }

    [Theory]
    [InlineData("conversationTurns", 5001)]
    [InlineData("toolExecutions", 5001)]
    [InlineData("fileChanges", 5001)]
    [InlineData("toolFailures", 501)]
    [InlineData("activityEvents", 501)]
    public async Task Events_MoreItemsInAListThanItsCap_Returns400(string list, int count)
    {
        var sessionId = NewId();
        await PostOk("/api/sessions/start", StartBody(sessionId, NewId()));
        var body = new Dictionary<string, object>
        {
            ["sessionId"] = sessionId,
            [list] = Enumerable.Range(0, count).Select(_ => Item(list)).ToList()
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("events")]
    public async Task Ingest_MissingSessionId_Returns400(string action)
    {
        var path = action == "start" ? "/api/sessions/start" : $"/api/sessions/{NewId()}/events";
        object body = action == "start"
            ? new { developerEmail = "dev@team.dev", startedAt = _past }
            : new { conversationTurns = new[] { Turn(NewId()) } };

        var response = await _client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private (string Path, object Body) Payload(string kind, string sessionId)
    {
        var events = $"/api/sessions/{sessionId}/events";
        return kind switch
        {
            "start" => ("/api/sessions/start", StartBody(sessionId, NewId())),
            "end" => ($"/api/sessions/{sessionId}/end", EndBody(sessionId, NewId())),
            "turn" => (events, new { sessionId, conversationTurns = new[] { Turn(NewId()) } }),
            "toolExecution" => (events, new { sessionId, toolExecutions = new[] { Item("toolExecutions") } }),
            "fileChange" => (events, new { sessionId, fileChanges = new[] { Item("fileChanges") } }),
            "toolFailure" => (events, new { sessionId, toolFailures = new[] { Item("toolFailures") } }),
            "activity" => (events, new { sessionId, activityEvents = new[] { Item("activityEvents") } }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private object Item(string list) => list switch
    {
        "conversationTurns" => Turn(NewId()),
        "toolExecutions" => new { id = NewId(), toolName = "Bash", timestamp = _past },
        "fileChanges" => new { id = NewId(), filePath = "src/a.cs" },
        "toolFailures" => new { id = NewId(), toolName = "Bash", at = _past },
        "activityEvents" => new { state = 0, at = _past },
        _ => throw new ArgumentOutOfRangeException(nameof(list), list, null)
    };

    private object Turn(string id) => new
    {
        id,
        role = "assistant",
        timestamp = _past,
        model = "claude-sonnet-4-5",
        inputTokens = 100,
        outputTokens = 20
    };

    private object StartBody(string sessionId, string? eventId) => new
    {
        sessionId,
        eventId,
        developerEmail = "delivery@team.dev",
        repoPath = "C:/repos/test",
        startedAt = _past
    };

    private object EndBody(string sessionId, string? eventId) => new { sessionId, eventId, endedAt = _past };

    private async Task<JsonElement> PostOk(string path, object body)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string?> Ids(JsonElement body, string outcome) =>
        body.GetProperty("results").GetProperty(outcome).EnumerateArray().Select(e => e.GetString()).ToList();

    // Everything an ingest can change about a session, read straight from the database.
    private async Task<string> Snapshot(string sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
        var s = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        return JsonSerializer.Serialize(new
        {
            Status = s.Status.ToString(),
            s.EndedAt,
            s.SummaryJson,
            s.Title,
            s.LastActivityAt,
            s.ActivityState,
            s.ActivityStateAt,
            s.ToolErrorCount,
            s.LastToolErrorName,
            s.LastToolErrorAt,
            s.TotalInputTokens,
            s.TotalOutputTokens,
            s.TotalCacheCreationInputTokens,
            s.TotalCacheReadInputTokens,
            s.EstimatedCostUsd,
            Turns = await db.ConversationTurns.CountAsync(x => x.SessionId == sessionId),
            Tools = await db.ToolExecutions.CountAsync(x => x.SessionId == sessionId),
            Files = await db.FileChanges.CountAsync(x => x.SessionId == sessionId),
            Failures = await db.SessionToolFailures.CountAsync(x => x.SessionId == sessionId)
        });
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
