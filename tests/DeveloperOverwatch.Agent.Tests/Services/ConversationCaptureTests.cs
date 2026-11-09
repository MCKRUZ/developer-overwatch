using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

// Fixtures mirror the transcript shape Claude Code writes today (measured 2026-11-09): one API
// response is split across several JSONL lines — one per content block — and every one of those
// lines repeats the SAME message id and the SAME usage object. Counting usage per line, or only on
// lines that carry text, both get totals wrong.
public sealed class ConversationCaptureTests : IDisposable
{
    private readonly ConversationCapture _sut =
        new(Substitute.For<ILogger<ConversationCapture>>());

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-capture-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string WriteTranscript(string name, params string[] lines)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Assistant(
        string messageId, string blockJson, int input = 3, int output = 50, int cacheWrite = 400,
        int cacheRead = 9000, string model = "claude-opus-5-5", string uuid = "", string ts = "2026-01-01T00:00:01Z") =>
        $$$"""{"type":"assistant","uuid":"{{{(uuid == "" ? Guid.NewGuid().ToString() : uuid)}}}","message":{"id":"{{{messageId}}}","role":"assistant","model":"{{{model}}}","content":[{{{blockJson}}}],"usage":{"input_tokens":{{{input}}},"output_tokens":{{{output}}},"cache_creation_input_tokens":{{{cacheWrite}}},"cache_read_input_tokens":{{{cacheRead}}}}},"timestamp":"{{{ts}}}"}""";

    private const string TextBlock = """{"type":"text","text":"Here is the answer."}""";
    private const string ThinkingBlock = """{"type":"thinking","thinking":"hmm"}""";
    private const string ToolUseBlock = """{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"ls"}}""";

    private static string UserPrompt(string uuid, string text) =>
        $$"""{"type":"user","uuid":"{{uuid}}","message":{"role":"user","content":"{{text}}"},"timestamp":"2026-01-01T00:00:00Z"}""";

    private static string UserToolResult(string uuid) =>
        $$"""{"type":"user","uuid":"{{uuid}}","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"file.txt"}]},"timestamp":"2026-01-01T00:00:02Z"}""";

    // Claude Code writes a top-level "gitBranch" on user and assistant lines.
    private static string OnBranch(string line, string branch) =>
        line.Insert(1, $"\"gitBranch\":\"{branch}\",");

    [Fact]
    public async Task CaptureFromFileAsync_GitBranchOnLines_IsCarriedOnPromptAndResponse()
    {
        var path = WriteTranscript("s.jsonl",
            OnBranch(UserPrompt("u1", "go"), "main"),
            OnBranch(Assistant("msg_A", ThinkingBlock), "feat/meter"),
            OnBranch(Assistant("msg_A", TextBlock), "feat/meter"),
            Assistant("msg_B", TextBlock));

        var result = await _sut.CaptureFromFileAsync(path, 0);

        result.Turns.Select(t => t.GitBranch).Should().Equal("main", "feat/meter", null);
    }

    [Fact]
    public async Task CaptureFromFileAsync_EmptyGitBranch_IsNull()
    {
        var path = WriteTranscript("s.jsonl", OnBranch(UserPrompt("u1", "go"), ""));

        var result = await _sut.CaptureFromFileAsync(path, 0);

        result.Turns.Single().GitBranch.Should().BeNull();
    }

    [Fact]
    public async Task CaptureFromFileAsync_SubagentWithMeta_NamesEveryTurnAndNeverShipsDescription()
    {
        var path = WriteTranscript("agent-a1.jsonl",
            Assistant("msg_S1", TextBlock),
            Assistant("msg_S2", ToolUseBlock));
        File.WriteAllText(Path.Combine(_dir.FullName, "agent-a1.meta.json"),
            """{"agentType":"Explore","description":"find X"}""");

        var result = await _sut.CaptureFromFileAsync(path, 0, asSubagent: true);

        result.Turns.Should().HaveCount(2).And.OnlyContain(t => t.SubagentName == "Explore");
        System.Text.Json.JsonSerializer.Serialize(result.Turns).Should().NotContain("find X");
    }

    [Fact]
    public async Task CaptureFromFileAsync_SubagentWithoutMeta_HasNullName()
    {
        var path = WriteTranscript("agent-a1.jsonl", Assistant("msg_S1", TextBlock));

        var result = await _sut.CaptureFromFileAsync(path, 0, asSubagent: true);

        result.Turns.Single().SubagentName.Should().BeNull();
    }

    [Fact]
    public async Task CaptureFromFileAsync_SubagentWithMalformedMeta_StillReturnsTurnsWithNullName()
    {
        var path = WriteTranscript("agent-a1.jsonl", Assistant("msg_S1", TextBlock));
        File.WriteAllText(Path.Combine(_dir.FullName, "agent-a1.meta.json"), "{\"agentType\":");

        var result = await _sut.CaptureFromFileAsync(path, 0, asSubagent: true);

        result.TotalLines.Should().Be(1);
        result.Turns.Single().SubagentName.Should().BeNull();
    }

    [Fact]
    public void FindProjectDir_PathThatMatchesNoProjectDir_ReturnsNull()
    {
        var unmatchablePath = Path.Combine(
            Path.GetTempPath(), $"overwatch-no-match-{Guid.NewGuid():N}", $"sub-{Guid.NewGuid():N}");

        _sut.FindProjectDir(unmatchablePath).Should().BeNull();
    }

    [Fact]
    public async Task CaptureFromFileAsync_MissingTranscriptFile_ReturnsEmptyWithoutThrowing()
    {
        var missingPath = Path.Combine(_dir.FullName, "missing.jsonl");

        var result = await _sut.CaptureFromFileAsync(missingPath, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().BeEmpty();
        result.TotalLines.Should().Be(0);
    }

    [Fact]
    public async Task CaptureFromFileAsync_ParsesTextContentAndUsage_SkippingLinesBeforeCursor()
    {
        var path = WriteTranscript("s.jsonl",
            UserPrompt("u-1", "first turn"),
            Assistant("msg_A", $"{ThinkingBlock},{TextBlock}", input: 10, output: 20, cacheWrite: 5, cacheRead: 1));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 1, ct: CancellationToken.None);

        result.TotalLines.Should().Be(2);
        var turn = result.Turns.Should().ContainSingle().Which;
        turn.Role.Should().Be("assistant");
        turn.Content.Should().Be("Here is the answer.");
        turn.InputTokens.Should().Be(10);
        turn.OutputTokens.Should().Be(20);
        turn.CacheCreationInputTokens.Should().Be(5);
        turn.CacheReadInputTokens.Should().Be(1);
        turn.Model.Should().Be("claude-opus-5-5");
    }

    [Fact]
    public async Task CaptureFromFileAsync_ResponseSplitAcrossLines_CountsItsUsageExactlyOnce()
    {
        // Three lines, one API response. The old parser booked usage once per text-bearing line,
        // so two text blocks doubled it while a response with no text block lost it entirely.
        var path = WriteTranscript("s.jsonl",
            Assistant("msg_A", ThinkingBlock),
            Assistant("msg_A", TextBlock),
            Assistant("msg_A", TextBlock),
            Assistant("msg_A", ToolUseBlock));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        var turn = result.Turns.Should().ContainSingle().Which;
        turn.InputTokens.Should().Be(3);
        turn.OutputTokens.Should().Be(50);
        turn.CacheCreationInputTokens.Should().Be(400);
        turn.CacheReadInputTokens.Should().Be(9000);
        turn.Content.Should().Be("Here is the answer.\nHere is the answer.");
    }

    [Fact]
    public async Task CaptureFromFileAsync_StreamedLinesWithGrowingUsage_KeepsTheLargestValuePerField()
    {
        // Older Claude Code builds wrote partial output_tokens on early lines of a response.
        var path = WriteTranscript("s.jsonl",
            Assistant("msg_A", TextBlock, output: 1),
            Assistant("msg_A", ToolUseBlock, output: 75));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().ContainSingle().Which.OutputTokens.Should().Be(75);
    }

    [Theory]
    [InlineData(ToolUseBlock)]
    [InlineData(ThinkingBlock)]
    public async Task CaptureFromFileAsync_ResponseWithNoTextBlock_StillRecordsItsUsage(string block)
    {
        var path = WriteTranscript("s.jsonl", Assistant("msg_A", block));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        var turn = result.Turns.Should().ContainSingle().Which;
        turn.Content.Should().BeNull();
        turn.CacheReadInputTokens.Should().Be(9000);
        turn.OutputTokens.Should().Be(50);
    }

    [Fact]
    public async Task CaptureFromFileAsync_NonIntegerUsageValue_DoesNotStallTheTranscript()
    {
        // An unreadable number must not abort the whole capture: that would return the old cursor
        // every time and the session would never ship another turn.
        var odd = Assistant("msg_A", TextBlock).Replace("\"output_tokens\":50", "\"output_tokens\":1.5");
        var path = WriteTranscript("s.jsonl", odd, Assistant("msg_B", TextBlock));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.TotalLines.Should().Be(2);
        result.Turns.Should().HaveCount(2);
        result.Turns[0].OutputTokens.Should().BeNull();
        result.Turns[0].CacheReadInputTokens.Should().Be(9000);
    }

    [Fact]
    public async Task CaptureFromFileAsync_RequestCopyLines_ProduceNoTurns()
    {
        // Claude Code also logs a copy of each outgoing request ("api-request-blob"), which embeds
        // earlier messages. Those are not new conversation and carry no usage of their own.
        var path = WriteTranscript("s.jsonl",
            """{"type":"api-request-blob","hash":"087e","message":{"role":"assistant","content":[{"type":"text","text":"old reply"}]}}""",
            """{"type":"api-request-blob","hash":"0a1b","message":{"role":"user","content":"old prompt"}}""",
            Assistant("msg_A", TextBlock));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().ContainSingle().Which.SourceId.Should().Contain("msg_A");
    }

    [Fact]
    public async Task CaptureFromFileAsync_ToolResultUserLine_ProducesNoTurn()
    {
        var path = WriteTranscript("s.jsonl", UserToolResult("u-9"));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().BeEmpty();
        result.TotalLines.Should().Be(1);
    }

    [Fact]
    public async Task CaptureFromFileAsync_SameContentInTwoTranscripts_YieldsIdenticalSourceIds()
    {
        // A resumed or forked session copies earlier lines (same message ids, same line uuids)
        // into a new transcript. Stable source ids are what let the server count them once.
        var lines = new[] { UserPrompt("u-1", "hi"), Assistant("msg_A", TextBlock, uuid: "a-1") };
        var original = WriteTranscript("original.jsonl", lines);
        var resumed = WriteTranscript("resumed.jsonl", lines);

        var first = await _sut.CaptureFromFileAsync(original, fromLineIndex: 0, ct: CancellationToken.None);
        var second = await _sut.CaptureFromFileAsync(resumed, fromLineIndex: 0, ct: CancellationToken.None);

        first.Turns.Select(t => t.SourceId).Should().Equal(second.Turns.Select(t => t.SourceId));
        first.Turns.Select(t => t.SourceId).Should().OnlyHaveUniqueItems();
        first.Turns[1].SourceId.Should().Contain("msg_A");
        first.Turns[0].SourceId.Should().Contain("u-1");
    }

    [Fact]
    public async Task CaptureFromFileAsync_PartialFinalLine_DoesNotAdvanceCursorPastIt()
    {
        // The transcript is read while Claude Code may still be writing it. A half-written last
        // line must be re-read next time, not skipped forever.
        var complete = Assistant("msg_B", TextBlock);
        var path = Path.Combine(_dir.FullName, "s.jsonl");
        await File.WriteAllTextAsync(path, Assistant("msg_A", TextBlock) + "\n" + complete[..40]);

        var first = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        first.Turns.Should().ContainSingle();
        first.TotalLines.Should().Be(1);

        await File.WriteAllTextAsync(path, Assistant("msg_A", TextBlock) + "\n" + complete + "\n");
        var second = await _sut.CaptureFromFileAsync(path, first.TotalLines, ct: CancellationToken.None);

        second.Turns.Should().ContainSingle().Which.SourceId.Should().Contain("msg_B");
        second.TotalLines.Should().Be(2);
    }

    [Fact]
    public async Task CaptureFromFileAsync_MalformedLineMidFile_IsSkippedAndCursorMovesPastIt()
    {
        var path = WriteTranscript("s.jsonl", "{not json", Assistant("msg_A", TextBlock));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().ContainSingle();
        result.TotalLines.Should().Be(2);
    }

    [Fact]
    public async Task CaptureFromFileAsync_AsSubagent_ReturnsOnlyAssistantUsageWithRealSplitAndModel()
    {
        var path = WriteTranscript("agent-a1.jsonl",
            UserPrompt("u-1", "Investigate the bug"),
            Assistant("msg_S1", ToolUseBlock, input: 2, output: 30, cacheWrite: 100, cacheRead: 5000, model: "claude-haiku-5-5"),
            UserToolResult("u-2"),
            Assistant("msg_S2", TextBlock, input: 1, output: 70, cacheWrite: 50, cacheRead: 6000, model: "claude-haiku-5-5"));

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, asSubagent: true, ct: CancellationToken.None);

        result.Turns.Should().HaveCount(2).And.OnlyContain(t => t.Role == "subagent" && t.Model == "claude-haiku-5-5");
        result.Turns.Sum(t => t.OutputTokens).Should().Be(100);
        result.Turns.Sum(t => t.CacheReadInputTokens).Should().Be(11000);
        result.Turns.Sum(t => t.InputTokens).Should().Be(3);
    }

    [Fact]
    public void FindSubagentTranscripts_ReturnsOnlyJsonlFilesInTheSessionsSubagentsFolder()
    {
        var transcript = WriteTranscript("sess-1.jsonl", UserPrompt("u-1", "hi"));
        var subDir = Directory.CreateDirectory(Path.Combine(_dir.FullName, "sess-1", "subagents"));
        File.WriteAllText(Path.Combine(subDir.FullName, "agent-b.jsonl"), "");
        File.WriteAllText(Path.Combine(subDir.FullName, "agent-a.jsonl"), "");
        File.WriteAllText(Path.Combine(subDir.FullName, "agent-a.meta.json"), "{}");
        File.WriteAllText(Path.Combine(subDir.FullName, "agent-a.prefix.json"), "{}");

        var found = _sut.FindSubagentTranscripts(transcript);

        found.Select(Path.GetFileName).Should().Equal("agent-a.jsonl", "agent-b.jsonl");
    }

    [Fact]
    public void FindSubagentTranscripts_NoSubagentsFolder_ReturnsEmpty()
    {
        var transcript = WriteTranscript("sess-2.jsonl", UserPrompt("u-1", "hi"));

        _sut.FindSubagentTranscripts(transcript).Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureFromFileAsync_TaskNotificationLine_IsNotBookedAsUsage()
    {
        // The <subagent_tokens> figure in a completion notice is the helper's final context size,
        // not its usage (measured: 80,289 reported vs 1,445,256 real tokens). Usage now comes from
        // the subagent's own transcript, so the notice itself must contribute nothing.
        var path = WriteTranscript("s.jsonl",
            """{"type":"queue-operation","operation":"enqueue","timestamp":"2026-01-01T00:05:00Z","content":"<task-notification>\n<task-id>a1</task-id>\n<usage><subagent_tokens>36019</subagent_tokens></usage>\n</task-notification>"}""");

        var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, ct: CancellationToken.None);

        result.Turns.Should().BeEmpty();
    }
}
