namespace DeveloperOverwatch.Application.Models;

using DeveloperOverwatch.Domain.Metering;

/// <summary>A budget's spend so far in its current period, which is the viewer's local day or calendar month.</summary>
/// <param name="PeriodStartUtc">Local midnight that opened the period.</param>
/// <param name="PeriodEndUtc">Local midnight that closes it.</param>
/// <param name="SpentUsd">Estimated cost of turns from <paramref name="PeriodStartUtc"/> to now.</param>
/// <param name="RemainingUsd">Amount less spend; negative when over.</param>
/// <param name="UsedFraction">Spend divided by amount; above 1 when over.</param>
public sealed record BudgetStatusDto(
    BudgetDto Budget,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    decimal SpentUsd,
    decimal RemainingUsd,
    decimal UsedFraction,
    BudgetLevel Level,
    PaceDto Pace,
    ForecastDto Forecast);
