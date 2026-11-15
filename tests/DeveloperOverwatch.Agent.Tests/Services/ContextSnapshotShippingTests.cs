using System.Text.Json;
using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static DeveloperOverwatch.Agent.Tests.Services.ContextLines;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The snapshot passes the same gates as the turns: it ships only beside new main-transcript turns, only
/// when dated at or after the session's floor, never with backfill, and at most once per ship.</summary>
public sealed class ContextSnapshotShippingTests : IDisposable
{
    private static readonly DateTime Floor = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-ship-context-");
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly string _transcript;

    public ContextSnapshotShippingTests()
    {
        _transcript = Path.Combine(_dir.FullName, "s1.jsonl");
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private static string At(int minutes) => Floor.AddMinutes(minutes).ToString("o");

    private static string Prompt(int i, string ts) =>
        $$"""{"type":"user","uuid":"u-{{i}}","message":{"role":"user","content":"prompt {{i}}"},"timestamp":"{{ts}}"}""";

    private void WriteMain(string requestTs, params string[] turns) =>
        File.WriteAllLines(_transcript,
            [Shape("h1", [new('s', 400)], Tool("mcp__github__a", "d")), ApiRequest("h1", requestTs), .. turns,
                Assistant("msg_1", 10, 990, ts: At(2))]);

    [Fact]
    public async Task Ship_SendsTheSnapshotOnTheFirstPostOnly_WithItsFields()
    {
        WriteMain(At(1), Enumerable.Range(0, ConversationShipper.MaxTurnsPerPost + 5).Select(i => Prompt(i, At(1))).ToArray());

        await ShipAsync(new OpenSession("s1", Floor));

        var bodies = SentBodies();
        bodies.Should().HaveCount(2);
        bodies[1]["contextSnapshot"].Should().BeNull();
        var snapshot = bodies[0]["contextSnapshot"]!.AsObject();
        snapshot.Select(p => p.Key).Should().Equal(
            "takenAt", "claudeCodeVersion", "model", "measuredContextTokens", "categories", "tools", "compactions");
        snapshot["takenAt"]!.GetValue<string>().Should().Be(Floor.AddMinutes(1).ToString("o"));
        snapshot["measuredContextTokens"]!.GetValue<long>().Should().Be(1000);
        snapshot["categories"]![0]!.ToJsonString().Should().Be("""{"category":"systemPrompt","server":null,"tokens":100}""");
        snapshot["tools"]![0]!.ToJsonString().Should().Be(
            """{"group":"github","isMcp":true,"toolCount":1,"tokensPerRequest":8,"used":false}""");
        bodies[0]["conversationTurns"]![0]!["turnKind"]!.GetValue<string>().Should().Be("prompt");
    }

    [Fact]
    public async Task SnapshotDatedBeforeTheFloor_IsNotSent_ThoughTheTurnsAre()
    {
        WriteMain(At(-1), Prompt(1, At(1)));

        await ShipAsync(new OpenSession("s1", Floor));

        var body = SentBodies().Should().ContainSingle().Subject;
        body["conversationTurns"]!.AsArray().Should().HaveCount(2);
        body["contextSnapshot"].Should().BeNull();
    }

    [Fact]
    public async Task Backfill_NeverSendsASnapshot()
    {
        WriteMain(At(1), Prompt(1, At(1)));

        await ShipAsync(new OpenSession("s1", DateTime.MinValue), ""","isBackfill":true""");

        var body = SentBodies().Should().ContainSingle().Subject;
        body["conversationTurns"]!.AsArray().Should().NotBeEmpty();
        body["contextSnapshot"].Should().BeNull();
    }

    [Fact]
    public async Task OnlySubagentLinesAreNew_NoSnapshotIsSent()
    {
        WriteMain(At(1), Prompt(1, At(1)));
        var subagents = Directory.CreateDirectory(Path.Combine(_dir.FullName, "s1", "subagents"));
        File.WriteAllLines(Path.Combine(subagents.FullName, "agent-a.jsonl"), [Assistant("msg_sub", 5, 50, ts: At(3))]);
        _cursors.GetAsync("s1", Arg.Any<CancellationToken>()).Returns(File.ReadAllLines(_transcript).Length);

        await ShipAsync(new OpenSession("s1", Floor));

        var body = SentBodies().Should().ContainSingle().Subject;
        body["conversationTurns"]!.AsArray().Single()!["role"]!.GetValue<string>().Should().Be("subagent");
        body["contextSnapshot"].Should().BeNull();
    }

    [Fact]
    public async Task NoCurrentShape_SendsANullSnapshot()
    {
        File.WriteAllLines(_transcript, [Prompt(1, At(1)), Assistant("msg_1", 10, 990, ts: At(2))]);

        await ShipAsync(new OpenSession("s1", Floor));

        var body = SentBodies().Should().ContainSingle().Subject;
        body.AsObject().ContainsKey("contextSnapshot").Should().BeTrue();
        body["contextSnapshot"].Should().BeNull();
    }

    // A server that refuses the snapshot (an older API, or a value it rejects) must not cost the turns their delivery:
    // the same turns are posted again at once without it, and the transcript cursor still advances.
    [Fact]
    public async Task SnapshotRefused_ResendsTheSameTurnsWithoutIt()
    {
        WriteMain(At(1));
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => ((string)call[1]).Contains("\"contextSnapshot\":{") ? ApiResult.Rejected : ApiResult.Success);

        await ShipAsync(new OpenSession("s1", Floor));

        var bodies = SentBodies();
        bodies.Should().HaveCount(2);
        bodies[0]["contextSnapshot"].Should().NotBeNull();
        bodies[1]["contextSnapshot"].Should().BeNull();
        bodies[1]["conversationTurns"]!.ToJsonString().Should().Be(bodies[0]["conversationTurns"]!.ToJsonString());
        await _cursors.Received().SetAsync("s1", File.ReadAllLines(_transcript).Length, Arg.Any<CancellationToken>());
    }

    private async Task ShipAsync(OpenSession session, string markerExtra = "")
    {
        var redactor = new PrivacyRedactor([]);
        var shipper = new ConversationShipper(
            _api, redactor, new ConversationCapture(Substitute.For<ILogger<ConversationCapture>>()), _cursors,
            Substitute.For<IUsageStore>(), NullLogger.Instance, localMode: false, Floor);
        var marker = new QueuedEvent
        {
            Id = "m", SessionId = "s1", EventType = EventType.ConversationTurn, CreatedAt = DateTime.UtcNow,
            PayloadJson = $$"""{"transcriptPath":{{JsonSerializer.Serialize(_transcript)}}{{markerExtra}}}"""
        };

        await shipper.ShipAsync(session, marker, CancellationToken.None);
    }

    private List<JsonNode> SentBodies() =>
        _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IApiClient.SendSessionEventsAsync))
            .Select(c => JsonNode.Parse((string)c.GetArguments()[1]!)!)
            .ToList();
}
