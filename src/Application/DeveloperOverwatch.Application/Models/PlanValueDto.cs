namespace DeveloperOverwatch.Application.Models;

/// <summary>A plan's price against the API-equivalent value of the usage it covered this local month so far.</summary>
/// <param name="MonthStartUtc">Local midnight that opened the month.</param>
/// <param name="ApiValueUsd">Estimated API cost of the developer's turns from <paramref name="MonthStartUtc"/> to now.</param>
/// <param name="ValueMultiple"><paramref name="ApiValueUsd"/> divided by <paramref name="MonthlyPriceUsd"/>.</param>
public sealed record PlanValueDto(
    string DeveloperEmail,
    string PlanName,
    decimal MonthlyPriceUsd,
    DateTime MonthStartUtc,
    decimal ApiValueUsd,
    decimal ValueMultiple);
