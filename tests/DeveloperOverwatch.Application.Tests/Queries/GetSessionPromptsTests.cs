namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessionPrompts;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

/// <summary>Cost per prompt: each prompt the developer typed is charged with everything it set off up to the
/// next prompt, so the rows must add up to the session and only a typed prompt may open a new row.</summary>
public class GetSessionPromptsTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly ISessionInsightRepository _repo = Substitute.For<ISessionInsightRepository>();
    private int _index;

    public GetSessionPromptsTests() => _repo.SessionExistsAsync("s-1", Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task Handle_UnknownSession_Fails()
    {
        var result = await Handle("nope");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoTurns_ReturnsEmpty()
    {
        Rows();

        (await Handle()).Value.Should().BeEmpty();
    }

    // Injected text (skills, hooks, hand-backs) and a compaction summary are not the developer asking for something.
    [Fact]
    public async Task Handle_OnlyTypedPromptsOpenARow()
    {
        Rows(
            User("prompt", "fix the build"), Assistant(1.00m),
            User("injected", "skill body"), Assistant(0.50m),
            User("compactSummary", "summary"), Assistant(0.25m),
            User("prompt", "now the tests"), Assistant(2.00m));

        var prompts = (await Handle()).Value!;

        prompts.Select(p => (p.Index, p.Label, p.TurnCount, p.EstimatedCostUsd)).Should().Equal(
            (1, "fix the build", 6, 1.75m),
            (2, "now the tests", 2, 2.00m));
    }

    // Agents older than the classification send no kind; their user turns are the developer's prompts.
    [Fact]
    public async Task Handle_TurnsWithoutKind_UserTurnsOpenRows()
    {
        Rows(User(null, "old agent prompt"), Assistant(1m), User(null, "second"), Assistant(1m));

        (await Handle()).Value!.Select(p => p.Index).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_TurnsBeforeFirstPrompt_GoToIndexZeroWithNoLabel()
    {
        Rows(Assistant(0.10m), User("prompt", "hello"), Assistant(1m));

        var prompts = (await Handle()).Value!;

        prompts[0].Should().BeEquivalentTo(new { Index = 0, Label = (string?)null, TurnCount = 1, EstimatedCostUsd = 0.10m });
        prompts[1].Index.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SubagentTurns_ChargedToPromptAndSplitOut()
    {
        Rows(User("prompt", "research"), Assistant(1m), Subagent(3m), Subagent(0.5m));

        var prompt = (await Handle()).Value!.Single();

        prompt.EstimatedCostUsd.Should().Be(4.5m);
        prompt.SubagentCostUsd.Should().Be(3.5m);
    }

    [Fact]
    public async Task Handle_SubagentTurnWithoutName_StillCountsAsSubagentCost()
    {
        Rows(User("prompt", "research"), Assistant(1m), Subagent(3m) with { SubagentName = null });

        (await Handle()).Value!.Single().SubagentCostUsd.Should().Be(3m);
    }

    [Fact]
    public async Task Handle_SumsTokensAsLong()
    {
        Rows(User("prompt", "big"), Assistant(1m, input: int.MaxValue, output: int.MaxValue, write: 1, read: 2));

        var prompt = (await Handle()).Value!.Single();

        prompt.InputTokens.Should().Be(int.MaxValue);
        prompt.TotalTokens.Should().Be(2L * int.MaxValue + 3);
    }

    // The rows are a split of the session, so together they must equal the session's total.
    [Fact]
    public async Task Handle_RowsAddUpToEveryTurnsCost()
    {
        var turns = new[] { Assistant(0.3m), User("prompt", "a"), Assistant(1m), Subagent(2m), User("injected", "x"), User("prompt", "b"), Assistant(4m) };
        Rows(turns);

        var prompts = (await Handle()).Value!;

        prompts.Sum(p => p.EstimatedCostUsd).Should().Be(turns.Sum(t => t.EstimatedCostUsd ?? 0m));
        prompts.Sum(p => p.TurnCount).Should().Be(turns.Length);
    }

    [Theory]
    [InlineData("  fix\n\n  the\tbuild  ", "fix the build")]
    [InlineData("   \n ", null)]
    [InlineData(null, null)]
    public async Task Handle_Label_CollapsesWhitespace(string? text, string? expected)
    {
        Rows(User("prompt", text), Assistant(1m));

        (await Handle()).Value!.Single().Label.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_LongLabel_CutTo80WithEllipsis()
    {
        Rows(User("prompt", new string('a', 200)), Assistant(1m));

        var label = (await Handle()).Value!.Single().Label!;

        label.Should().HaveLength(80).And.EndWith("…").And.StartWith(new string('a', 79));
    }

    // Cutting between the two halves of an emoji would leave an invalid character at the end of the label.
    [Fact]
    public async Task Handle_LongLabel_NeverSplitsASurrogatePair()
    {
        Rows(User("prompt", new string('a', 78) + "😀😀😀"), Assistant(1m));

        var label = (await Handle()).Value!.Single().Label!;

        label.Should().Be(new string('a', 78) + "…");
    }

    private Task<Domain.Common.Result<IReadOnlyList<PromptCost>>> Handle(string sessionId = "s-1") =>
        new GetSessionPromptsQueryHandler(_repo).Handle(new GetSessionPromptsQuery(sessionId), CancellationToken.None);

    private void Rows(params SessionTurnCostRow[] rows) =>
        _repo.GetTurnCostRowsAsync("s-1", Arg.Any<CancellationToken>()).Returns(rows);

    private SessionTurnCostRow User(string? kind, string? text) =>
        Row(SessionTurnCostRow.UserRole, kind, null, text, null, 0, 0, 0, 0);

    private SessionTurnCostRow Assistant(decimal cost, int input = 10, int output = 5, int write = 0, int read = 0) =>
        Row("assistant", null, null, null, cost, input, output, write, read);

    private SessionTurnCostRow Subagent(decimal cost) =>
        Row(SessionTurnCostRow.SubagentRole, null, "Explore", null, cost, 10, 5, 0, 0);

    private SessionTurnCostRow Row(
        string role, string? kind, string? subagent, string? text, decimal? cost, int input, int output, int write, int read)
    {
        _index++;
        return new SessionTurnCostRow(
            _index, T0.AddSeconds(_index), role, kind, subagent, text, input, output, write, read, cost);
    }
}
