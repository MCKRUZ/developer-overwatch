namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessionSubagentBreakdown;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

/// <summary>The session's usage split between the main thread and each subagent; the rows are a split of the
/// session, so they must add up to it.</summary>
public class GetSessionSubagentBreakdownTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly ISessionInsightRepository _repo = Substitute.For<ISessionInsightRepository>();

    public GetSessionSubagentBreakdownTests() =>
        _repo.SessionExistsAsync("s-1", Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task Handle_UnknownSession_Fails()
    {
        (await Handle("nope")).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_GroupsBySubagent_MainThreadAsNull_CostDescending()
    {
        Rows(Turn(null, 1m, 10), Turn(null, 0m, 0), Turn("Explore", 3m, 20), Turn("Plan", 0.5m, 5), Turn("Explore", 1m, 5));

        var rows = (await Handle()).Value!;

        rows.Select(r => (r.SubagentName, r.TurnCount, r.EstimatedCostUsd, r.InputTokens)).Should().Equal(
            ("Explore", 2, 4m, 25L),
            (null, 2, 1m, 10L),
            ("Plan", 1, 0.5m, 5L));
    }

    [Fact]
    public async Task Handle_RowsAddUpToSession()
    {
        var turns = new[] { Turn(null, 1.25m, 10), Turn("Explore", 3m, 20), Turn("Plan", 0.5m, 5) };
        Rows(turns);

        var rows = (await Handle()).Value!;

        rows.Sum(r => r.EstimatedCostUsd).Should().Be(4.75m);
        rows.Sum(r => r.TotalTokens).Should().Be(turns.Sum(t => (long)t.InputTokens!.Value + t.OutputTokens!.Value));
    }

    // The Agent leaves the name empty when a subagent's meta file is missing; its turns are still a subagent's,
    // and must not be added to the main thread, whose context they never shared.
    [Fact]
    public async Task Handle_SubagentTurnWithoutName_IsAnUnnamedSubagentNotTheMainThread()
    {
        Rows(Turn(null, 1m, 10), Turn("Explore", 3m, 20) with { SubagentName = null });

        var rows = (await Handle()).Value!;

        rows.Select(r => (r.SubagentName, r.EstimatedCostUsd)).Should().Equal(
            (GetSessionSubagentBreakdownQueryHandler.UnnamedSubagent, 3m),
            (null, 1m));
    }

    [Fact]
    public async Task Handle_NoTurns_ReturnsEmpty()
    {
        Rows();

        (await Handle()).Value.Should().BeEmpty();
    }

    private Task<Domain.Common.Result<IReadOnlyList<SubagentUsage>>> Handle(string sessionId = "s-1") =>
        new GetSessionSubagentBreakdownQueryHandler(_repo)
            .Handle(new GetSessionSubagentBreakdownQuery(sessionId), CancellationToken.None);

    private void Rows(params SessionTurnCostRow[] rows) =>
        _repo.GetTurnCostRowsAsync("s-1", Arg.Any<CancellationToken>()).Returns(rows);

    private static SessionTurnCostRow Turn(string? subagent, decimal cost, int input) => new(
        0, T0, subagent is null ? "assistant" : SessionTurnCostRow.SubagentRole, null, subagent, null,
        input, 2, 0, 0, cost);
}
