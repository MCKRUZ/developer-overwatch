namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessionContext;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

/// <summary>A session's context fill, peak, breakout and watch list, as the session page shows them.</summary>
public class GetSessionContextTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly AsOf = new(2026, 11, 9);
    private readonly ISessionInsightRepository _repo = Substitute.For<ISessionInsightRepository>();
    private readonly IModelContextWindowProvider _windows = Substitute.For<IModelContextWindowProvider>();

    public GetSessionContextTests()
    {
        _windows.AsOf.Returns(AsOf);
        _windows.ContextWindowTokensFor(Arg.Any<string?>()).Returns(1_000_000);
        _windows.ContextWindowTokensFor("claude-sonnet-small").Returns(200_000);
        Header(new SessionContextHeader(null, null, 0, null, null));
    }

    [Fact]
    public async Task Handle_UnknownSession_Fails()
    {
        _repo.GetContextHeaderAsync("s-1", Arg.Any<CancellationToken>()).Returns((SessionContextHeader?)null);

        (await Handle()).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NothingMeasured_CurrentAndPeakAreNull_NoBreakout()
    {
        var context = (await Handle()).Value!;

        context.WindowsAsOf.Should().Be(AsOf);
        context.CurrentContextTokens.Should().BeNull();
        context.CurrentFill.Should().BeNull();
        context.PeakContextTokens.Should().BeNull();
        context.PeakFill.Should().BeNull();
        context.Breakout.Should().BeNull();
        context.UnusedTools.Should().BeNull("the watch list needs a snapshot to know which tools were sent");
    }

    [Fact]
    public async Task Handle_CurrentAndPeak_FillAgainstEachModelsWindow()
    {
        Header(new SessionContextHeader(150_000, "claude-sonnet-small", 2, "Bash", T0));
        _repo.GetLatestMainThreadContextAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(new ContextReading("claude-opus-5-5", 250_000));

        var context = (await Handle()).Value!;

        context.Should().BeEquivalentTo(new
        {
            CurrentModel = "claude-opus-5-5", CurrentContextTokens = 250_000L, CurrentWindowTokens = 1_000_000L, CurrentFill = 0.25,
            PeakContextTokens = 150_000L, PeakModel = "claude-sonnet-small", PeakWindowTokens = 200_000L, PeakFill = 0.75,
            ToolErrorCount = 2, LastToolErrorName = "Bash", LastToolErrorAt = T0,
        });
    }

    // Sessions stored before the peak's model was recorded have a peak but no model: the current model is the best guess.
    [Fact]
    public async Task Handle_PeakWithoutModel_UsesCurrentModel()
    {
        Header(new SessionContextHeader(100_000, null, 0, null, null));
        _repo.GetLatestMainThreadContextAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(new ContextReading("claude-sonnet-small", 50_000));

        var context = (await Handle()).Value!;

        context.PeakModel.Should().Be("claude-sonnet-small");
        context.PeakFill.Should().Be(0.5);
    }

    [Fact]
    public async Task Handle_Snapshot_CategoriesLargestFirst_ConversationLast_CamelCaseNames()
    {
        Snapshot(categories:
        [
            new(ContextCategory.Conversation, null, 900),
            new(ContextCategory.SystemPrompt, null, 30),
            new(ContextCategory.McpTools, "github", 50),
            new(ContextCategory.McpTools, "linear", 50),
            new(ContextCategory.MemoryFiles, null, 70),
        ]);

        var breakout = (await Handle()).Value!.Breakout!;

        breakout.Categories.Select(c => (c.Category, c.Server, c.Tokens)).Should().Equal(
            ("memoryFiles", null, 70L),
            ("mcpTools", "github", 50L),
            ("mcpTools", "linear", 50L),
            ("systemPrompt", null, 30L),
            ("conversation", null, 900L));
        breakout.Should().BeEquivalentTo(new
        {
            TakenAt = T0, ClaudeCodeVersion = "2.1.40", Model = "claude-opus-5-5",
            MeasuredContextTokens = 1_100L, WindowTokens = 1_000_000L, Fill = 0.0011,
        });
    }

    [Fact]
    public async Task Handle_Snapshot_CompactionsNewestFirst()
    {
        Snapshot(compactions:
        [
            new(T0.AddHours(-3), "auto", 180_000, 20_000),
            new(T0.AddHours(-1), "manual", 120_000, 15_000),
        ]);

        var compactions = (await Handle()).Value!.Breakout!.Compactions;

        compactions.Select(c => c.Trigger).Should().Equal("manual", "auto");
    }

    // Only tools sent on every request and never called are waste; the costliest come first.
    [Fact]
    public async Task Handle_Snapshot_UnusedToolsOnly_CostliestFirst()
    {
        Snapshot(tools:
        [
            new("Bash", false, 1, 900, true),
            new("linear", true, 30, 5_000, false),
            new("NotebookEdit", false, 1, 400, false),
            new("asana", true, 20, 5_000, false),
        ]);

        var unused = (await Handle()).Value!.UnusedTools!;

        unused.Select(u => (u.Group, u.IsMcp, u.ToolCount, u.TokensPerRequest)).Should().Equal(
            ("asana", true, 20, 5_000L),
            ("linear", true, 30, 5_000L),
            ("NotebookEdit", false, 1, 400L));
    }

    [Fact]
    public async Task Handle_SnapshotWithNoUnusedTools_ReturnsEmptyListNotNull()
    {
        Snapshot(tools: [new("Bash", false, 1, 900, true)]);

        (await Handle()).Value!.UnusedTools.Should().BeEmpty();
    }

    private Task<Domain.Common.Result<SessionContextView>> Handle() =>
        new GetSessionContextQueryHandler(_repo, _windows).Handle(new GetSessionContextQuery("s-1"), CancellationToken.None);

    private void Header(SessionContextHeader header) =>
        _repo.GetContextHeaderAsync("s-1", Arg.Any<CancellationToken>()).Returns(header);

    private void Snapshot(
        SessionContextCategory[]? categories = null,
        SessionContextToolGroup[]? tools = null,
        SessionContextCompaction[]? compactions = null) =>
        _repo.GetContextSnapshotAsync("s-1", Arg.Any<CancellationToken>()).Returns(SessionContextSnapshot.Create(
            "s-1", T0, "2.1.40", "claude-opus-5-5", 1_100,
            categories ?? [new(ContextCategory.Conversation, null, 1_100)],
            tools ?? [],
            compactions ?? []));
}
