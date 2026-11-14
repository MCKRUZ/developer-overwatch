namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;

/// <summary>One developer's 5-hour window card: listed while a window is open, and always when the developer has
/// a plan. Plan fields are explicit nulls without a plan (or without a window limit); <see cref="Window"/> is an
/// explicit null when no window is open.</summary>
public sealed record DeveloperWindowDto(
    string DeveloperEmail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PlanName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? WindowLimitUsd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] WindowDto? Window);
