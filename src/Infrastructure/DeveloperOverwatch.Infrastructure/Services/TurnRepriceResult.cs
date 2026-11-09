namespace DeveloperOverwatch.Infrastructure.Services;

/// <summary>How many turns were repriced and how many sessions' totals were recomputed.</summary>
public sealed record TurnRepriceResult(int TurnCount, int SessionCount);
