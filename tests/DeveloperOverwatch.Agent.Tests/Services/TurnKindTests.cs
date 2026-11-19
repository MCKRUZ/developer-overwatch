using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Only a prompt the developer typed starts a new prompt on the server. Skill bodies, hook text, subagent
/// hand-backs and compaction summaries are user lines too, and are told apart by their flags.</summary>
public sealed class TurnKindTests : IDisposable
{
    private readonly ConversationCapture _sut = new(Substitute.For<ILogger<ConversationCapture>>());
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-kind-");

    public void Dispose() => _dir.Delete(recursive: true);

    private static string User(string flags) =>
        $$"""{"type":"user","uuid":"u1",{{flags}}"message":{"role":"user","content":"text"},"timestamp":"2026-01-01T00:00:00Z"}""";

    [Theory]
    [InlineData("", "prompt")]
    [InlineData("\"origin\":{\"kind\":\"human\"},", "prompt")]
    [InlineData("\"isMeta\":false,\"isCompactSummary\":false,", "prompt")]
    [InlineData("\"isMeta\":true,", "injected")]
    [InlineData("\"isMeta\":true,\"origin\":{\"kind\":\"human\"},", "injected")]
    [InlineData("\"origin\":{\"kind\":\"peer\"},", "injected")]
    [InlineData("\"origin\":{\"kind\":\"task-notification\"},", "injected")]
    [InlineData("\"isCompactSummary\":true,", "compactSummary")]
    [InlineData("\"isCompactSummary\":true,\"isMeta\":true,", "compactSummary")]
    [InlineData("\"isCompactSummary\":true,\"origin\":{\"kind\":\"peer\"},", "compactSummary")]
    public async Task UserTurn_GetsItsKindFromItsFlags(string flags, string expected)
    {
        var path = Path.Combine(_dir.FullName, "s.jsonl");
        File.WriteAllLines(path, [User(flags)]);

        var result = await _sut.CaptureFromFileAsync(path, 0);

        result.Turns.Single().TurnKind.Should().Be(expected);
    }

    [Fact]
    public async Task ResponsesAndSubagentTurns_HaveNoKind()
    {
        const string response = """{"type":"assistant","uuid":"a1","message":{"id":"msg_1","role":"assistant","model":"m","content":[{"type":"text","text":"hi"}],"usage":{"input_tokens":1}},"timestamp":"2026-01-01T00:00:01Z"}""";
        var path = Path.Combine(_dir.FullName, "s.jsonl");
        File.WriteAllLines(path, [response]);

        (await _sut.CaptureFromFileAsync(path, 0)).Turns.Single().TurnKind.Should().BeNull();
        (await _sut.CaptureFromFileAsync(path, 0, asSubagent: true)).Turns.Single().TurnKind.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_CarriesTheKind_WithOrWithoutText(bool usageOnly)
    {
        var turns = new[]
        {
            new CapturedTurn("uuid:u1", "user", "go", DateTime.UtcNow, TurnKind: "prompt"),
            new CapturedTurn("msg:m1", "assistant", "ok", DateTime.UtcNow)
        };

        var json = ConversationPayload.Build("s1", null, turns, new PrivacyRedactor([]), usageOnly, null);

        var sent = JsonNode.Parse(json)!["conversationTurns"]!.AsArray();
        sent[0]!["turnKind"]!.GetValue<string>().Should().Be("prompt");
        sent[1]!.AsObject().ContainsKey("turnKind").Should().BeTrue();
        sent[1]!["turnKind"].Should().BeNull();
    }
}
