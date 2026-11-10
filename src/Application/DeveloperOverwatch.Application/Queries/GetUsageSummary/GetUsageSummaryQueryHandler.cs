namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using MediatR;

/// <summary>The database sums turns per UTC 15-minute slot; this places each slot on its local day in the
/// viewer's zone (DST and half-hour offsets included), drops slots outside the period, and rolls the rest up.</summary>
public sealed class GetUsageSummaryQueryHandler : IRequestHandler<GetUsageSummaryQuery, Result<UsageSummary>>
{
    public static readonly TimeSpan BurnLookback = TimeSpan.FromMinutes(15);

    private readonly IUsageRepository _repository;
    private readonly ITokenPricingCalculator _pricing;
    private readonly TimeProvider _time;

    public GetUsageSummaryQueryHandler(IUsageRepository repository, ITokenPricingCalculator pricing, TimeProvider time)
    {
        _repository = repository;
        _pricing = pricing;
        _time = time;
    }

    public async Task<Result<UsageSummary>> Handle(GetUsageSummaryQuery request, CancellationToken cancellationToken)
    {
        // The validator has already checked every field below.
        var (from, to, granularity) = (request.From!.Value, request.To!.Value, request.Granularity!);
        UsageCalendar.TryFindZone(request.TimeZone, out var zone);
        var filter = new UsageFilter(request.Developer, request.Project);

        var slots = new Dictionary<UsageDimension, IReadOnlyList<(DateOnly Day, UsageSlotRow Row)>>();
        foreach (var dimension in Enum.GetValues<UsageDimension>())
            slots[dimension] = await SlotsInPeriod(from, to, zone, filter, dimension, cancellationToken);

        // Every turn has exactly one model key (null included), so any one dimension holds all the period's turns.
        var all = slots[UsageDimension.Model];
        var totals = UsageAggregation.Totals(all.Select(s => s.Row));

        return Result<UsageSummary>.Success(new UsageSummary(
            from, to, granularity, request.TimeZone!, _pricing.PricesAsOf, totals,
            Buckets(all, from, to, granularity),
            Splits(slots, totals.EstimatedCostUsd),
            await Burn(filter, cancellationToken),
            request.Developer, request.Project));
    }

    private async Task<IReadOnlyList<(DateOnly Day, UsageSlotRow Row)>> SlotsInPeriod(
        DateOnly from, DateOnly to, TimeZoneInfo zone, UsageFilter filter, UsageDimension dimension,
        CancellationToken ct)
    {
        var (fromUtc, toUtc) = UsageCalendar.QueryWindow(from, to);
        var rows = await _repository.GetSlotsAsync(fromUtc, toUtc, filter, dimension, ct);
        return rows
            .Select(r => (Day: UsageCalendar.LocalDate(r.SlotStartUtc, zone), Row: r))
            .Where(s => s.Day >= from && s.Day <= to)
            .ToList();
    }

    private static IReadOnlyList<UsageBucket> Buckets(
        IReadOnlyList<(DateOnly Day, UsageSlotRow Row)> slots, DateOnly from, DateOnly to, string granularity)
    {
        var byBucket = slots.ToLookup(s => UsageCalendar.BucketStart(s.Day, granularity), s => s.Row);
        return UsageCalendar.BucketStarts(from, to, granularity)
            .Select(start => new UsageBucket(start, UsageAggregation.Totals(byBucket[start])))
            .ToList();
    }

    private static UsageSplits Splits(
        Dictionary<UsageDimension, IReadOnlyList<(DateOnly Day, UsageSlotRow Row)>> slots, decimal periodCost)
    {
        IReadOnlyList<UsageSplitRow> Of(UsageDimension dimension) =>
            UsageAggregation.Split(slots[dimension].Select(s => s.Row), dimension, periodCost);

        return new UsageSplits(
            Of(UsageDimension.Model), Of(UsageDimension.Project), Of(UsageDimension.Session),
            Of(UsageDimension.Subagent), Of(UsageDimension.Branch), Of(UsageDimension.Developer),
            Of(UsageDimension.Machine), Of(UsageDimension.Agent));
    }

    private async Task<UsageBurn> Burn(UsageFilter filter, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var points = await _repository.GetTurnPointsAsync(now - BurnLookback, now, filter, ct);
        var burn = BurnRate.Over(points, now, BurnLookback);
        return new UsageBurn(burn.TokensPerMinute, burn.CostPerMinute, (int)BurnLookback.TotalMinutes);
    }
}
