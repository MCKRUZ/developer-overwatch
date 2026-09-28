using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

// FindProjectDir resolves against the user's real ~/.claude/projects directory (a hardcoded
// static path), so only the "definitely does not exist" case can be exercised deterministically.
public sealed class ConversationCaptureTests
{
    private readonly ConversationCapture _sut =
        new(Substitute.For<ILogger<ConversationCapture>>());

    [Fact]
    public void FindProjectDir_PathThatMatchesNoProjectDir_ReturnsNull()
    {
        var unmatchablePath = Path.Combine(
            Path.GetTempPath(),
            $"overwatch-no-match-{Guid.NewGuid():N}",
            $"sub-{Guid.NewGuid():N}");

        var result = _sut.FindProjectDir(unmatchablePath);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureFromFileAsync_MissingTranscriptFile_ReturnsEmptyWithoutThrowing()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(), $"overwatch-missing-{Guid.NewGuid():N}.jsonl");

        var result = await _sut.CaptureFromFileAsync(missingPath, fromLineIndex: 0, CancellationToken.None);

        result.Turns.Should().BeEmpty();
        result.TotalLines.Should().Be(0);
    }

    [Fact]
    public async Task CaptureFromFileAsync_ParsesTextContentAndUsage_SkippingLinesBeforeCursor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"overwatch-transcript-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(path,
        [
            """{"message":{"role":"user","content":"first turn"},"timestamp":"2026-01-01T00:00:00Z"}""",
            """{"message":{"role":"assistant","content":[{"type":"thinking","thinking":"skip me"},{"type":"text","text":"second turn"}],"usage":{"input_tokens":10,"output_tokens":20,"cache_creation_input_tokens":5,"cache_read_input_tokens":1}},"timestamp":"2026-01-01T00:00:01Z"}"""
        ]);

        try
        {
            var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 1, CancellationToken.None);

            result.TotalLines.Should().Be(2);
            result.Turns.Should().HaveCount(1);
            var turn = result.Turns[0];
            turn.Role.Should().Be("assistant");
            turn.Content.Should().Be("second turn");
            turn.InputTokens.Should().Be(10);
            turn.OutputTokens.Should().Be(20);
            turn.CacheCreationInputTokens.Should().Be(5);
            turn.CacheReadInputTokens.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CaptureFromFileAsync_SubagentCompletion_AttributesTokensToItsResolvedModel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"overwatch-transcript-{Guid.NewGuid():N}.jsonl");
        var launchLine = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":[{"type":"text","text":"Async agent launched"}]}]},"toolUseResult":{"isAsync":true,"status":"async_launched","agentId":"agent-abc123","resolvedModel":"claude-opus-5-gce"},"timestamp":"2026-01-01T00:00:00Z"}""";
        var completionLine = """{"type":"queue-operation","operation":"enqueue","timestamp":"2026-01-01T00:05:00Z","content":"<task-notification>\n<task-id>agent-abc123</task-id>\n<status>completed</status>\n<usage><subagent_tokens>36019</subagent_tokens><tool_uses>14</tool_uses><duration_ms>290503</duration_ms></usage>\n</task-notification>"}""";
        await File.WriteAllLinesAsync(path, [launchLine, completionLine]);

        try
        {
            var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 0, CancellationToken.None);

            var subagentTurn = result.Turns.Should().ContainSingle(t => t.Role == "subagent").Which;
            subagentTurn.Model.Should().Be("claude-opus-5-gce");
            subagentTurn.OutputTokens.Should().Be(36019);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CaptureFromFileAsync_SubagentLaunchBeforeCursor_StillResolvesModelForLaterCompletion()
    {
        // The launch record can be far behind the cursor (a long-running subagent), so the model
        // map must be built from the true start of the file, not from fromLineIndex onward.
        var path = Path.Combine(Path.GetTempPath(), $"overwatch-transcript-{Guid.NewGuid():N}.jsonl");
        var launchLine = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":[{"type":"text","text":"Async agent launched"}]}]},"toolUseResult":{"isAsync":true,"status":"async_launched","agentId":"agent-xyz789","resolvedModel":"claude-sonnet-5-gce"},"timestamp":"2026-01-01T00:00:00Z"}""";
        var fillerLine = """{"message":{"role":"user","content":"unrelated"},"timestamp":"2026-01-01T00:01:00Z"}""";
        var completionLine = """{"type":"queue-operation","operation":"enqueue","timestamp":"2026-01-01T00:05:00Z","content":"<task-notification>\n<task-id>agent-xyz789</task-id>\n<usage><subagent_tokens>500</subagent_tokens></usage>\n</task-notification>"}""";
        await File.WriteAllLinesAsync(path, [launchLine, fillerLine, completionLine]);

        try
        {
            var result = await _sut.CaptureFromFileAsync(path, fromLineIndex: 2, CancellationToken.None);

            var subagentTurn = result.Turns.Should().ContainSingle(t => t.Role == "subagent").Which;
            subagentTurn.Model.Should().Be("claude-sonnet-5-gce");
            subagentTurn.OutputTokens.Should().Be(500);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
