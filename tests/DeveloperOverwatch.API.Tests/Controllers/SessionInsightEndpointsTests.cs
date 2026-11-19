namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// The three session-level reads end to end, asserted on the JSON itself. The dashboard's tests build their data
/// from factories, so only this test notices if the server spells a category or a field differently.
/// </summary>
public class SessionInsightEndpointsTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public SessionInsightEndpointsTests(OverwatchWebApplicationFactory factory) =>
        _client = factory.CreateAuthenticatedClient();

    [Theory]
    [InlineData("context")]
    [InlineData("prompts")]
    [InlineData("subagent-breakdown")]
    public async Task Get_UnknownSession_Returns404(string read)
    {
        var response = await _client.GetAsync($"/api/sessions/{NewId()}/{read}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_AfterIngest_WritesCamelCaseCategoriesAndOrderedLists()
    {
        var sessionId = await IngestedSession();

        var json = await GetJson($"/api/sessions/{sessionId}/context");

        json.GetProperty("windowsAsOf").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        json.GetProperty("currentContextTokens").GetInt64().Should().Be(1_000);
        json.GetProperty("currentModel").GetString().Should().Be("claude-opus-5-5");
        json.GetProperty("toolErrorCount").GetInt32().Should().Be(0);
        var breakout = json.GetProperty("breakout");
        breakout.GetProperty("categories").EnumerateArray()
            .Select(c => c.GetProperty("category").GetString())
            .Should().Equal("mcpTools", "systemPrompt", "conversation");
        breakout.GetProperty("categories")[0].GetProperty("server").GetString().Should().Be("github");
        breakout.GetProperty("compactions").EnumerateArray()
            .Select(c => c.GetProperty("trigger").GetString()).Should().Equal("manual", "auto");
        json.GetProperty("unusedTools").EnumerateArray()
            .Select(t => t.GetProperty("group").GetString()).Should().Equal("github");
    }

    [Fact]
    public async Task Prompts_AfterIngest_OneRowPerTypedPrompt()
    {
        var sessionId = await IngestedSession();

        var json = await GetJson($"/api/sessions/{sessionId}/prompts");

        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("index").GetInt32().Should().Be(1);
        json[0].GetProperty("label").GetString().Should().Be("fix the build");
        json[0].GetProperty("turnCount").GetInt32().Should().Be(3);
        json[0].GetProperty("inputTokens").GetInt64().Should().Be(1_300);
        json[0].TryGetProperty("subagentCostUsd", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SubagentBreakdown_AfterIngest_MainThreadRowHasNoName()
    {
        var sessionId = await IngestedSession();

        var json = await GetJson($"/api/sessions/{sessionId}/subagent-breakdown");

        var names = json.EnumerateArray().Select(SubagentName).ToList();
        names.Should().BeEquivalentTo([null, "Explore"]);
    }

    // The server omits null members, so the main thread's row has no name at all.
    private static string? SubagentName(JsonElement row) =>
        row.TryGetProperty("subagentName", out var name) ? name.GetString() : null;

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<string> IngestedSession()
    {
        var sessionId = NewId();
        var start = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            sessionId, eventId = NewId(), developerEmail = "insights@team.dev", startedAt = _past.AddMinutes(-1)
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await _client.PostAsync($"/api/sessions/{sessionId}/events",
            new StringContent(Body(sessionId), Encoding.UTF8, "application/json"));
        events.StatusCode.Should().Be(HttpStatusCode.OK, await events.Content.ReadAsStringAsync());
        return sessionId;
    }

    private string Body(string sessionId) => $$"""
        {
          "sessionId": "{{sessionId}}",
          "conversationTurns": [
            { "id": "{{NewId()}}", "turnIndex": 0, "role": "user", "content": "  fix\n the build ",
              "timestamp": "{{Iso(_past)}}", "isDecision": false, "containsRedactedSecret": false, "turnKind": "prompt" },
            { "id": "{{NewId()}}", "turnIndex": 1, "role": "assistant", "content": "done",
              "inputTokens": 1000, "outputTokens": 50, "model": "claude-opus-5-5",
              "timestamp": "{{Iso(_past.AddSeconds(5))}}", "isDecision": false, "containsRedactedSecret": false },
            { "id": "{{NewId()}}", "turnIndex": 0, "role": "subagent", "subagentName": "Explore", "content": "looked",
              "inputTokens": 300, "outputTokens": 20, "model": "claude-haiku-5-5",
              "timestamp": "{{Iso(_past.AddSeconds(3))}}", "isDecision": false, "containsRedactedSecret": false }
          ],
          "toolExecutions": [],
          "fileChanges": [],
          "contextSnapshot": {
            "takenAt": "{{Iso(_past.AddSeconds(5))}}",
            "claudeCodeVersion": "2.1.40",
            "model": "claude-opus-5-5",
            "measuredContextTokens": 1000,
            "categories": [
              { "category": "systemPrompt", "server": null, "tokens": 100 },
              { "category": "conversation", "server": null, "tokens": 600 },
              { "category": "mcpTools", "server": "github", "tokens": 300 }
            ],
            "tools": [
              { "group": "github", "isMcp": true, "toolCount": 3, "tokensPerRequest": 300, "used": false },
              { "group": "Bash", "isMcp": false, "toolCount": 1, "tokensPerRequest": 50, "used": true }
            ],
            "compactions": [
              { "at": "{{Iso(_past.AddHours(-2))}}", "trigger": "auto", "preTokens": 180000, "postTokens": 20000 },
              { "at": "{{Iso(_past.AddHours(-1))}}", "trigger": "manual", "preTokens": 90000, "postTokens": 15000 }
            ]
          }
        }
        """;

    private static string Iso(DateTime utc) => utc.ToString("O");

    private static string NewId() => Guid.NewGuid().ToString("N");
}
