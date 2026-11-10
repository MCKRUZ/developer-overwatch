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
/// Tool execution and file change ids are primary keys, so an id stored under one session can never be
/// stored under another. Reporting it as accepted would fail the insert with a 500, which the Agent
/// retries forever and which holds the whole session; it is a duplicate, and nothing changes.
/// </summary>
public class IngestCrossSessionDuplicateTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public IngestCrossSessionDuplicateTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Theory]
    [InlineData("toolExecutions")]
    [InlineData("fileChanges")]
    public async Task Events_IdStoredUnderAnotherSession_IsDuplicateAndChangesNothing(string list)
    {
        var (first, second) = (await StartSession(), await StartSession());
        var id = NewId();
        var (status, results) = await Post(first, list, id);
        status.Should().Be(HttpStatusCode.OK);
        Ids(results, "accepted").Should().Equal(id);

        (status, results) = await Post(second, list, id);

        status.Should().Be(HttpStatusCode.OK, results.ToString());
        Ids(results, "duplicate").Should().Equal(id);
        Ids(results, "accepted").Should().BeEmpty();
        results.GetProperty("rejected").EnumerateArray().Should().BeEmpty();
        (await SessionsStoring(list, id)).Should().Equal(first);
        (await RowCount(list, second)).Should().Be(0);
    }

    // A file change counts toward its own session, so it may not hang off another session's tool, even
    // when the batch names that tool too (as a duplicate it is not stored again).
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Events_FileChangeOnAnotherSessionsTool_IsRejectedFkMissing(bool toolInBatch, bool sameSession)
    {
        var (first, second) = (await StartSession(), await StartSession());
        var toolId = NewId();
        (await Post(first, "toolExecutions", toolId)).Status.Should().Be(HttpStatusCode.OK);
        var target = sameSession ? first : second;
        var fileId = NewId();
        var file = new Dictionary<string, object?> { ["id"] = fileId, ["filePath"] = "src/a.cs", ["toolExecutionId"] = toolId };
        var body = new Dictionary<string, object> { ["sessionId"] = target, ["fileChanges"] = new[] { file } };
        if (toolInBatch)
            body["toolExecutions"] = new[] { Item("toolExecutions", toolId) };

        var (status, results) = await Post(target, body);

        status.Should().Be(HttpStatusCode.OK, results.ToString());
        if (sameSession)
        {
            Ids(results, "accepted").Should().Contain(fileId);
            return;
        }

        var rejected = results.GetProperty("rejected").EnumerateArray().Should().ContainSingle().Subject;
        rejected.GetProperty("id").GetString().Should().Be(fileId);
        rejected.GetProperty("reason").GetString().Should().StartWith("fk_missing");
        (await RowCount("fileChanges", second)).Should().Be(0);
    }

    private Task<(HttpStatusCode Status, JsonElement Results)> Post(string sessionId, string list, string id) =>
        Post(sessionId, new Dictionary<string, object> { ["sessionId"] = sessionId, [list] = new[] { Item(list, id) } });

    private async Task<(HttpStatusCode Status, JsonElement Results)> Post(string sessionId, Dictionary<string, object> body)
    {
        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", body);
        var text = await response.Content.ReadAsStringAsync();
        var results = response.IsSuccessStatusCode
            ? JsonDocument.Parse(text).RootElement.GetProperty("results").Clone()
            : JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone();
        return (response.StatusCode, results);
    }

    private async Task<IReadOnlyList<string>> SessionsStoring(string list, string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
        return list == "toolExecutions"
            ? await db.ToolExecutions.Where(t => t.Id == id).Select(t => t.SessionId).ToListAsync()
            : await db.FileChanges.Where(f => f.Id == id).Select(f => f.SessionId).ToListAsync();
    }

    private async Task<int> RowCount(string list, string sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
        return list == "toolExecutions"
            ? await db.ToolExecutions.CountAsync(t => t.SessionId == sessionId)
            : await db.FileChanges.CountAsync(f => f.SessionId == sessionId);
    }

    private static IReadOnlyList<string?> Ids(JsonElement results, string outcome) =>
        results.GetProperty(outcome).EnumerateArray().Select(e => e.GetString()).ToList();

    private Dictionary<string, object?> Item(string list, string id) => list == "toolExecutions"
        ? new() { ["id"] = id, ["toolName"] = "Bash", ["timestamp"] = _past }
        : new() { ["id"] = id, ["filePath"] = "src/a.cs" };

    private async Task<string> StartSession()
    {
        var sessionId = NewId();
        var response = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            sessionId,
            eventId = NewId(),
            developerEmail = "cross@team.dev",
            startedAt = _past
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return sessionId;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
