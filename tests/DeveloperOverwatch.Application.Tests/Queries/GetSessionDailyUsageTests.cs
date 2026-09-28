namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessionDailyUsage;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetSessionDailyUsageTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();

    [Fact]
    public async Task Handle_GroupsTurnsByCalendarDay_SummingTokensAndCost()
    {
        var day1 = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);
        var day1Later = new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc);

        _repo.GetTurnUsageRowsAsync("s1", Arg.Any<CancellationToken>()).Returns(new List<TurnUsageRow>
        {
            new(day1, "claude-sonnet-5", 100, 50, 0, 0, 0.01m),
            new(day1Later, "claude-sonnet-5", 200, 100, 10, 5, 0.02m),
            new(day2, "claude-opus", 300, 150, 0, 0, 0.05m),
        });

        var handler = new GetSessionDailyUsageQueryHandler(_repo);
        var result = await handler.Handle(new GetSessionDailyUsageQuery("s1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var days = result.Value!;
        days.Should().HaveCount(2);

        var firstDay = days[0];
        firstDay.Date.Should().Be(DateOnly.FromDateTime(day1));
        firstDay.TurnCount.Should().Be(2);
        firstDay.InputTokens.Should().Be(300);
        firstDay.OutputTokens.Should().Be(150);
        firstDay.CacheCreationInputTokens.Should().Be(10);
        firstDay.CacheReadInputTokens.Should().Be(5);
        firstDay.EstimatedCostUsd.Should().Be(0.03m);

        var secondDay = days[1];
        secondDay.Date.Should().Be(DateOnly.FromDateTime(day2));
        secondDay.TurnCount.Should().Be(1);
        secondDay.EstimatedCostUsd.Should().Be(0.05m);
    }

    [Fact]
    public async Task Handle_NoTurns_ReturnsEmptyList()
    {
        _repo.GetTurnUsageRowsAsync("s1", Arg.Any<CancellationToken>()).Returns(new List<TurnUsageRow>());

        var handler = new GetSessionDailyUsageQueryHandler(_repo);
        var result = await handler.Handle(new GetSessionDailyUsageQuery("s1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
