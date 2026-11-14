namespace DeveloperOverwatch.Application.Queries.GetPlanValues;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPlanValuesQueryHandler : IRequestHandler<GetPlanValuesQuery, Result<IReadOnlyList<PlanValueDto>>>
{
    private readonly IDeveloperPlanRepository _plans;
    private readonly IUsageRepository _usage;
    private readonly TimeProvider _time;

    public GetPlanValuesQueryHandler(IDeveloperPlanRepository plans, IUsageRepository usage, TimeProvider time)
    {
        _plans = plans;
        _usage = usage;
        _time = time;
    }

    public async Task<Result<IReadOnlyList<PlanValueDto>>> Handle(
        GetPlanValuesQuery request, CancellationToken cancellationToken)
    {
        UsageCalendar.TryFindZone(request.TimeZone, out var zone);
        var now = _time.GetUtcNow().UtcDateTime;
        var plans = await _plans.GetAllAsync(cancellationToken);
        if (plans.Count == 0)
            return Result<IReadOnlyList<PlanValueDto>>.Success([]);

        var today = UsageCalendar.LocalDate(now, zone);
        var monthStart = UsageCalendar.LocalMidnightUtc(new DateOnly(today.Year, today.Month, 1), zone);
        var slots = await _usage.GetSlotsAsync(monthStart, now.AddTicks(1), UsageFilter.None, cancellationToken);
        var valueByEmail = slots
            .GroupBy(s => s.DeveloperEmail, DeveloperEmail.Comparer)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.EstimatedCostUsd), DeveloperEmail.Comparer);

        return Result<IReadOnlyList<PlanValueDto>>.Success(plans
            .Select(p =>
            {
                var value = valueByEmail.GetValueOrDefault(p.DeveloperEmail);
                return new PlanValueDto(
                    p.DeveloperEmail, p.PlanName, p.MonthlyPriceUsd, monthStart, value, value / p.MonthlyPriceUsd);
            })
            .ToList());
    }
}
