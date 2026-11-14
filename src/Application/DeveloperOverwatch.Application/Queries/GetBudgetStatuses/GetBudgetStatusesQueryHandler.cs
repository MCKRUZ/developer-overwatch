namespace DeveloperOverwatch.Application.Queries.GetBudgetStatuses;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using MediatR;

public sealed class GetBudgetStatusesQueryHandler
    : IRequestHandler<GetBudgetStatusesQuery, Result<IReadOnlyList<BudgetStatusDto>>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IUsageRepository _usage;
    private readonly TimeProvider _time;

    public GetBudgetStatusesQueryHandler(IBudgetRepository budgets, IUsageRepository usage, TimeProvider time)
    {
        _budgets = budgets;
        _usage = usage;
        _time = time;
    }

    public async Task<Result<IReadOnlyList<BudgetStatusDto>>> Handle(
        GetBudgetStatusesQuery request, CancellationToken cancellationToken)
    {
        UsageCalendar.TryFindZone(request.TimeZone, out var zone);
        var now = _time.GetUtcNow().UtcDateTime;
        var budgets = await _budgets.GetAllAsync(cancellationToken);
        if (budgets.Count == 0)
            return Result<IReadOnlyList<BudgetStatusDto>>.Success([]);

        var today = UsageCalendar.LocalDate(now, zone);
        var periods = budgets.Select(b => b.Period).Distinct().ToDictionary(p => p, p => PeriodOf(p, today, zone));

        // One read from the earliest period start covers every budget. Local midnights fall on quarter hours,
        // which slots never straddle, so a slot belongs to a period exactly when it starts inside it.
        var slots = await _usage.GetSlotsAsync(
            periods.Values.Min(p => p.Start), now.AddTicks(1), UsageFilter.None, cancellationToken);

        return Result<IReadOnlyList<BudgetStatusDto>>.Success(
            budgets.Select(b => StatusOf(b, periods[b.Period], slots, now)).ToList());
    }

    private static (DateTime Start, DateTime End) PeriodOf(BudgetPeriod period, DateOnly today, TimeZoneInfo zone)
    {
        var first = period == BudgetPeriod.Daily ? today : new DateOnly(today.Year, today.Month, 1);
        var next = period == BudgetPeriod.Daily ? first.AddDays(1) : first.AddMonths(1);
        return (UsageCalendar.LocalMidnightUtc(first, zone), UsageCalendar.LocalMidnightUtc(next, zone));
    }

    private static BudgetStatusDto StatusOf(
        Budget budget, (DateTime Start, DateTime End) period, IReadOnlyList<UsageSlotRow> slots, DateTime now)
    {
        var spent = slots
            .Where(s => s.SlotStartUtc >= period.Start)
            .Where(s => budget.Scope == BudgetScope.Team || DeveloperEmail.Comparer.Equals(s.DeveloperEmail, budget.DeveloperEmail))
            .Sum(s => s.EstimatedCostUsd);
        var pace = Pace.Compute(spent, budget.AmountUsd, period.Start, period.End, now);
        var forecast = Forecast.Project(spent, budget.AmountUsd, period.Start, period.End, now);
        return new BudgetStatusDto(
            BudgetDto.From(budget), period.Start, period.End, spent, budget.AmountUsd - spent,
            pace.UsedFraction, BudgetThreshold.LevelOf(pace.UsedFraction), PaceDto.From(pace), ForecastDto.From(forecast));
    }
}
