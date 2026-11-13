namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Metering;

/// <summary>When a limit runs out at the average rate so far, as <see cref="Forecast.Project"/> found it.
/// <see cref="RunsOutAt"/> is set only for <see cref="ForecastKind.RunsOutWithinPeriod"/> and is otherwise written
/// as an explicit null.</summary>
public sealed record ForecastDto(
    ForecastKind Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? RunsOutAt)
{
    public static ForecastDto From(ForecastResult forecast) => new(forecast.Kind, forecast.RunsOutAt);
}
