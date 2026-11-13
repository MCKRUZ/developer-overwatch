namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetPlanValues;
using DeveloperOverwatch.Application.Tests.Persistence;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>Plan price against the API-equivalent value of the usage it covered, month to date in the viewer's
/// local month, on the real repositories and SQLite.</summary>
public sealed class GetPlanValuesTests : IDisposable
{
    private static readonly DateTime Now = Utc(2026, 11, 10, 17);

    private readonly UsageDatabase _data = new();

    public void Dispose() => _data.Dispose();

    // November starts at 04:00Z in New York (midnight EDT), so 03:59Z on the 1st still belongs to October.
    [Fact]
    public async Task Handle_PlansWithAndWithoutUsage_ReportMonthToDateValueAndMultipleByEmail()
    {
        await AddPlans(
            DeveloperPlan.Create("b@team.dev", "Pro", 100m, null, Now),
            DeveloperPlan.Create("a@team.dev", "Max", 200m, 10m, Now));
        await _data.Seed(
            Session("a", email: "a@team.dev", turns:
            [
                Turn(Utc(2026, 11, 1, 3, 59), cost: 100m),
                Turn(Utc(2026, 11, 1, 4, 0), cost: 1000m),
                Turn(Now, cost: 140m),
                Turn(Now.AddMinutes(1), cost: 400m), // after now
            ]),
            Session("x", email: "noplan@team.dev", turns: [Turn(Now.AddHours(-1), cost: 9m)]));

        var values = await Run();

        var monthStart = Utc(2026, 11, 1, 4);
        values.Should().BeEquivalentTo(
            [
                new PlanValueDto("a@team.dev", "Max", 200m, monthStart, 1140m, 5.7m),
                new PlanValueDto("b@team.dev", "Pro", 100m, monthStart, 0m, 0m),
            ],
            o => o.WithStrictOrdering());
        values[0].MonthStartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Handle_NoPlans_ReturnsAnEmptyList()
    {
        await _data.Seed(Session("x", turns: [Turn(Now.AddHours(-1), cost: 9m)]));

        (await Run()).Should().BeEmpty();
    }

    private async Task AddPlans(params DeveloperPlan[] plans)
    {
        _data.Db.DeveloperPlans.AddRange(plans);
        await _data.Db.SaveChangesAsync();
        _data.Db.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<PlanValueDto>> Run()
    {
        var handler = new GetPlanValuesQueryHandler(new DeveloperPlanRepository(_data.Db), _data.Repository, new FixedTime(Now));
        var result = await handler.Handle(new GetPlanValuesQuery("America/New_York"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }
}
