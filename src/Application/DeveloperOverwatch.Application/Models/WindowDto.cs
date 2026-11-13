namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Metering;

/// <summary>A developer's estimated open 5-hour window. The last four members measure spend against the plan's
/// window limit and are explicit nulls when the developer has no limit.</summary>
/// <param name="StartUtc">The turn that opened the window.</param>
/// <param name="ResetsAtUtc">When the window ends and its allowance resets.</param>
/// <param name="SpentUsd">Estimated cost of the window's turns so far.</param>
/// <param name="Tokens">All four token kinds of the window's turns so far.</param>
public sealed record WindowDto(
    DateTime StartUtc,
    DateTime ResetsAtUtc,
    decimal SpentUsd,
    long Tokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? UsedFraction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] BudgetLevel? Level,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] PaceDto? Pace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ForecastDto? Forecast);
