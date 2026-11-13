namespace DeveloperOverwatch.Application.Queries.GetUsageWindows;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using MediatR;

public sealed class GetUsageWindowsQueryHandler
    : IRequestHandler<GetUsageWindowsQuery, Result<IReadOnlyList<DeveloperWindowDto>>>
{
    // Windows chain from earlier activity, so UsageWindow.Current needs history back past the last 5-hour gap.
    private static readonly TimeSpan Lookback = TimeSpan.FromHours(24);

    private readonly IDeveloperPlanRepository _plans;
    private readonly IUsageRepository _usage;
    private readonly TimeProvider _time;

    public GetUsageWindowsQueryHandler(IDeveloperPlanRepository plans, IUsageRepository usage, TimeProvider time)
    {
        _plans = plans;
        _usage = usage;
        _time = time;
    }

    public async Task<Result<IReadOnlyList<DeveloperWindowDto>>> Handle(
        GetUsageWindowsQuery request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var plans = (await _plans.GetAllAsync(cancellationToken))
            .ToDictionary(p => p.DeveloperEmail, StringComparer.Ordinal);
        var points = await _usage.GetDeveloperTurnPointsAsync(now - Lookback, now, cancellationToken);

        var open = points
            .GroupBy(p => p.DeveloperEmail, StringComparer.Ordinal)
            .Select(g => (Email: g.Key, Points: g.ToList(), Window: UsageWindow.Current(g.Select(p => p.AtUtc), now)))
            .Where(d => d.Window is not null)
            .ToDictionary(
                d => d.Email,
                d => WindowOf(d.Points, d.Window!, plans.GetValueOrDefault(d.Email)?.WindowLimitUsd, now),
                StringComparer.Ordinal);

        var rows = plans.Keys.Union(open.Keys, StringComparer.Ordinal)
            .Select(email => new DeveloperWindowDto(
                email,
                plans.GetValueOrDefault(email)?.PlanName,
                plans.GetValueOrDefault(email)?.WindowLimitUsd,
                open.GetValueOrDefault(email)))
            .OrderBy(r => r.Window?.UsedFraction is null)
            .ThenByDescending(r => r.Window?.UsedFraction)
            .ThenBy(r => r.DeveloperEmail, StringComparer.Ordinal)
            .ToList();
        return Result<IReadOnlyList<DeveloperWindowDto>>.Success(rows);
    }

    private static WindowDto WindowOf(
        IReadOnlyList<DeveloperTurnPoint> points, UsageWindowResult window, decimal? limitUsd, DateTime now)
    {
        var inWindow = points.Where(p => p.AtUtc >= window.Start).ToList();
        var spent = inWindow.Sum(p => p.CostUsd);
        var tokens = inWindow.Sum(p => p.Tokens);
        if (limitUsd is not { } limit)
            return new WindowDto(window.Start, window.ResetsAt, spent, tokens, null, null, null, null);

        var pace = Pace.Compute(spent, limit, window.Start, window.ResetsAt, now);
        var forecast = Forecast.Project(spent, limit, window.Start, window.ResetsAt, now);
        return new WindowDto(
            window.Start, window.ResetsAt, spent, tokens, pace.UsedFraction, BudgetThreshold.LevelOf(pace.UsedFraction),
            PaceDto.From(pace), ForecastDto.From(forecast));
    }
}
