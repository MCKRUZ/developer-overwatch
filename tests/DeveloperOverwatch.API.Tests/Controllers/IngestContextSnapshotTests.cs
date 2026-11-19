namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The body exactly as the Agent writes it: camelCase category names, null fields written out, the folded
/// "(other servers)" row and "unknown" defaults. If the server could not bind it, every ship carrying a
/// snapshot would be refused and the dashboard would never get a context breakdown.
/// </summary>
public class IngestContextSnapshotTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public IngestContextSnapshotTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task Events_AgentBodyWithSnapshotAndTurnKind_StoresBoth()
    {
        var sessionId = await StartSession();
        var turnId = NewId();

        var response = await PostEvents(sessionId, AgentBody(sessionId, turnId, Snapshot(categoryName: "systemPrompt")));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
        (await db.ConversationTurns.SingleAsync(t => t.Id == turnId)).TurnKind.Should().Be("prompt");
        var snapshot = await db.SessionContextSnapshots.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
        snapshot.TakenAt.Should().Be(_past);
        snapshot.ClaudeCodeVersion.Should().Be("unknown");
        snapshot.Model.Should().Be("claude-opus-5-5");
        snapshot.MeasuredContextTokens.Should().Be(1000);
        snapshot.Categories.Select(c => (c.Category, c.Server, c.Tokens)).Should().Equal(
            (ContextCategory.SystemPrompt, null, 100L),
            (ContextCategory.McpTools, "(other servers)", 300L),
            (ContextCategory.Conversation, null, 600L));
        snapshot.Tools.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Group = "github", IsMcp = true, ToolCount = 1, TokensPerRequest = 8L, Used = false });
        snapshot.Compactions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Trigger = "auto", PreTokens = 180000L, PostTokens = 20000L });
    }

    // A refused snapshot sinks the request, which the Agent answers by resending the turns without it.
    [Fact]
    public async Task Events_SnapshotWithUnknownCategory_IsRefused()
    {
        var sessionId = await StartSession();

        var response = await PostEvents(sessionId, AgentBody(sessionId, NewId(), Snapshot(categoryName: "notACategory")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<HttpResponseMessage> PostEvents(string sessionId, string json) =>
        _client.PostAsync($"/api/sessions/{sessionId}/events", new StringContent(json, Encoding.UTF8, "application/json"));

    private string AgentBody(string sessionId, string turnId, string snapshot) => $$"""
        {
          "sessionId": "{{sessionId}}",
          "sessionTitle": null,
          "conversationTurns": [
            { "id": "{{turnId}}", "turnIndex": 0, "role": "user", "content": "fix it", "tokenCount": null,
              "inputTokens": null, "outputTokens": null, "cacheCreationInputTokens": null, "cacheReadInputTokens": null,
              "model": null, "gitBranch": null, "subagentName": null, "containsRedactedSecret": false,
              "timestamp": "{{Iso(_past)}}", "isDecision": false, "turnKind": "prompt" }
          ],
          "toolExecutions": [],
          "fileChanges": [],
          "contextSnapshot": {{snapshot}}
        }
        """;

    private string Snapshot(string categoryName) => $$"""
        {
          "takenAt": "{{Iso(_past)}}",
          "claudeCodeVersion": "unknown",
          "model": "claude-opus-5-5",
          "measuredContextTokens": 1000,
          "categories": [
            { "category": "{{categoryName}}", "server": null, "tokens": 100 },
            { "category": "mcpTools", "server": "(other servers)", "tokens": 300 },
            { "category": "conversation", "server": null, "tokens": 600 }
          ],
          "tools": [ { "group": "github", "isMcp": true, "toolCount": 1, "tokensPerRequest": 8, "used": false } ],
          "compactions": [ { "at": "{{Iso(_past.AddHours(-1))}}", "trigger": "auto", "preTokens": 180000, "postTokens": 20000 } ]
        }
        """;

    private static string Iso(DateTime utc) => utc.ToString("O");

    private async Task<string> StartSession()
    {
        var sessionId = NewId();
        var response = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            sessionId,
            eventId = NewId(),
            developerEmail = "snapshot@team.dev",
            startedAt = _past.AddMinutes(-1)
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return sessionId;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
