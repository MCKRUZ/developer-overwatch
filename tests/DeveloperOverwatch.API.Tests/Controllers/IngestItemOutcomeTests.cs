namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// One bad item in an events batch must be reported on its own, never sink the request. A 400 on the
/// whole batch would make the Agent treat every item in it as rejected, so a single item with a blank
/// or missing field would take good items down with it.
/// </summary>
public class IngestItemOutcomeTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly DateTime _past = DateTime.UtcNow.AddMinutes(-5);

    public IngestItemOutcomeTests(OverwatchWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
    }

    [Theory]
    [InlineData("conversationTurns", "role", false)]
    [InlineData("conversationTurns", "role", true)]
    [InlineData("toolExecutions", "toolName", false)]
    [InlineData("toolExecutions", "toolName", true)]
    [InlineData("fileChanges", "filePath", false)]
    [InlineData("fileChanges", "filePath", true)]
    [InlineData("toolFailures", "toolName", false)]
    [InlineData("toolFailures", "toolName", true)]
    [InlineData("toolFailures", "at", true)]
    public async Task Events_ItemWithBlankOrMissingRequiredField_RejectsOnlyThatItem(
        string list, string field, bool omitted)
    {
        var sessionId = await StartSession();
        var (good, bad) = (NewId(), NewId());
        var badItem = Item(list, bad);
        if (omitted)
            badItem.Remove(field);
        else
            badItem[field] = "";
        var body = new Dictionary<string, object>
        {
            ["sessionId"] = sessionId,
            [list] = new[] { Item(list, good), badItem }
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", body);

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var results = JsonDocument.Parse(text).RootElement.GetProperty("results");
        results.GetProperty("accepted").EnumerateArray().Select(e => e.GetString()).Should().Equal(good);
        var rejected = results.GetProperty("rejected").EnumerateArray().ToList();
        rejected.Should().ContainSingle();
        rejected[0].GetProperty("id").GetString().Should().Be(bad);
        rejected[0].GetProperty("reason").GetString().Should().Contain(field);
    }

    // An item without an id has nothing to report a rejection under, so the request is refused and
    // the Agent narrows the batch down to that item.
    [Theory]
    [InlineData("conversationTurns")]
    [InlineData("toolExecutions")]
    [InlineData("fileChanges")]
    [InlineData("toolFailures")]
    public async Task Events_ItemWithoutId_Returns400(string list)
    {
        var sessionId = await StartSession();
        var noId = Item(list, NewId());
        noId.Remove("id");
        var body = new Dictionary<string, object> { ["sessionId"] = sessionId, [list] = new[] { noId } };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Dictionary<string, object?> Item(string list, string id) => list switch
    {
        "conversationTurns" => new() { ["id"] = id, ["role"] = "assistant", ["timestamp"] = _past },
        "toolExecutions" => new() { ["id"] = id, ["toolName"] = "Bash", ["timestamp"] = _past },
        "fileChanges" => new() { ["id"] = id, ["filePath"] = "src/a.cs" },
        "toolFailures" => new() { ["id"] = id, ["toolName"] = "Bash", ["at"] = _past },
        _ => throw new ArgumentOutOfRangeException(nameof(list), list, null)
    };

    private async Task<string> StartSession()
    {
        var sessionId = NewId();
        var response = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            sessionId,
            eventId = NewId(),
            developerEmail = "outcome@team.dev",
            startedAt = _past
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return sessionId;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
