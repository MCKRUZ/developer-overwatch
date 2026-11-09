namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessionModelBreakdown;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetSessionModelBreakdownTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();

    [Fact]
    public async Task Handle_GroupsByModel_OrderedByCostDescending()
    {
        var now = DateTime.UtcNow;
        _repo.GetTurnUsageRowsAsync("s1", Arg.Any<CancellationToken>()).Returns(new List<TurnUsageRow>
        {
            new(now, "claude-haiku", 100, 50, 0, 0, 0.001m),
            new(now, "claude-opus", 100, 50, 0, 0, 5.00m),
            new(now, "claude-opus", 100, 50, 0, 0, 5.00m),
            new(now, null, 10, 5, 0, 0, 0.0001m),
        });

        var handler = new GetSessionModelBreakdownQueryHandler(_repo);
        var result = await handler.Handle(new GetSessionModelBreakdownQuery("s1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var models = result.Value!;
        models.Should().HaveCount(3);
        models[0].Model.Should().Be("claude-opus");
        models[0].TurnCount.Should().Be(2);
        models[0].EstimatedCostUsd.Should().Be(10.00m);
        models.Should().Contain(m => m.Model == "unknown");
    }

    [Fact]
    public async Task Handle_ExcludesZeroCostZeroTokenBuckets()
    {
        var now = DateTime.UtcNow;
        _repo.GetTurnUsageRowsAsync("s1", Arg.Any<CancellationToken>()).Returns(new List<TurnUsageRow>
        {
            new(now, "claude-sonnet-5", 100, 50, 0, 0, 1.00m),
            // Synthetic/decision-marker turns with no model and no usage — real rows, but not
            // meaningful in a cost breakdown.
            new(now, null, null, null, null, null, null),
            new(now, "<synthetic>", 0, 0, 0, 0, 0m),
        });

        var handler = new GetSessionModelBreakdownQueryHandler(_repo);
        var result = await handler.Handle(new GetSessionModelBreakdownQuery("s1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle().Which.Model.Should().Be("claude-sonnet-5");
    }
}
