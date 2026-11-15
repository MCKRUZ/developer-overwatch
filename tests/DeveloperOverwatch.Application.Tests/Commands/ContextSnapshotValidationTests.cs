namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Domain.Enums;
using FluentAssertions;
using Xunit;
using static IngestDatabase;

/// <summary>
/// A refused snapshot costs nothing: the Agent resends the same turns without it. A stored one that breaks a
/// column bound fails the whole save on SQL Server (SQLite ignores lengths, so no other test would notice),
/// and a negative or unknown value would reach the dashboard. So the whole request is refused instead.
/// </summary>
public sealed class ContextSnapshotValidationTests
{
    private static readonly IngestSessionEventsCommandValidator Validator = new();

    public static TheoryData<string, Func<ContextSnapshotDto, ContextSnapshotDto>> Invalid => new()
    {
        { "takenAt", s => s with { TakenAt = default } },
        { "claudeCodeVersion missing", s => s with { ClaudeCodeVersion = null } },
        { "claudeCodeVersion long", s => s with { ClaudeCodeVersion = new string('v', 51) } },
        { "model missing", s => s with { Model = " " } },
        { "model long", s => s with { Model = new string('m', 256) } },
        { "measured negative", s => s with { MeasuredContextTokens = -1 } },
        { "categories null", s => s with { Categories = null! } },
        { "categories over cap", s => s with { Categories = Repeat(21, new ContextCategoryDto { Tokens = 1 }) } },
        { "category undefined", s => s with { Categories = [new() { Category = (ContextCategory)99, Tokens = 1 }] } },
        { "category item null", s => s with { Categories = [null!] } },
        { "server long", s => s with { Categories = [new() { Category = ContextCategory.McpTools, Server = new string('s', 256) }] } },
        { "category tokens negative", s => s with { Categories = [new() { Tokens = -1 }] } },
        { "tools null", s => s with { Tools = null! } },
        { "tools over cap", s => s with { Tools = Repeat(201, Tool()) } },
        { "group missing", s => s with { Tools = [Tool() with { Group = null }] } },
        { "group long", s => s with { Tools = [Tool() with { Group = new string('g', 256) }] } },
        { "toolCount negative", s => s with { Tools = [Tool() with { ToolCount = -1 }] } },
        { "tokensPerRequest negative", s => s with { Tools = [Tool() with { TokensPerRequest = -1 }] } },
        { "compactions null", s => s with { Compactions = null! } },
        { "compactions over cap", s => s with { Compactions = Repeat(51, Compaction()) } },
        { "compaction at", s => s with { Compactions = [Compaction() with { At = default }] } },
        { "trigger missing", s => s with { Compactions = [Compaction() with { Trigger = "" }] } },
        { "trigger long", s => s with { Compactions = [Compaction() with { Trigger = new string('t', 21) }] } },
        { "preTokens negative", s => s with { Compactions = [Compaction() with { PreTokens = -1 }] } },
        { "postTokens negative", s => s with { Compactions = [Compaction() with { PostTokens = -1 }] } },
    };

    [Fact]
    public void Validate_AgentSnapshot_Passes() =>
        Validator.Validate(Command(Snapshot(Now))).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_NoSnapshot_Passes() =>
        Validator.Validate(Command(null)).IsValid.Should().BeTrue();

    // The Agent caps its lists at exactly these sizes and clamps strings to exactly these lengths.
    [Fact]
    public void Validate_EveryListAndStringAtItsBound_Passes()
    {
        var snapshot = Snapshot(Now) with
        {
            ClaudeCodeVersion = new string('v', 50),
            Model = new string('m', 255),
            Categories = Repeat(20, new ContextCategoryDto { Category = ContextCategory.McpTools, Server = new string('s', 255) }),
            Tools = Repeat(200, Tool() with { Group = new string('g', 255) }),
            Compactions = Repeat(50, Compaction() with { Trigger = new string('t', 20) }),
        };

        Validator.Validate(Command(snapshot)).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validate_InvalidSnapshot_RefusesTheRequest(string because, Func<ContextSnapshotDto, ContextSnapshotDto> breakIt) =>
        Validator.Validate(Command(breakIt(Snapshot(Now)))).IsValid.Should().BeFalse(because);

    private static IngestSessionEventsCommand Command(ContextSnapshotDto? snapshot) =>
        new() { SessionId = "s-1", ContextSnapshot = snapshot };

    private static ContextToolGroupDto Tool() => new() { Group = "github", IsMcp = true, ToolCount = 1, TokensPerRequest = 8 };

    private static ContextCompactionDto Compaction() =>
        new() { At = Now.AddHours(-1), Trigger = "auto", PreTokens = 180000, PostTokens = 20000 };

    private static IReadOnlyList<T> Repeat<T>(int count, T item) => Enumerable.Repeat(item, count).ToList();
}
