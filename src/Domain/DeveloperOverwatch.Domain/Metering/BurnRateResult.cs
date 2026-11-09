namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// Average consumption per minute over a lookback, as computed by <see cref="BurnRate.Over"/>.
/// </summary>
/// <param name="TokensPerMinute">Tokens consumed per minute of lookback.</param>
/// <param name="CostPerMinute">US dollars spent per minute of lookback.</param>
public sealed record BurnRateResult(double TokensPerMinute, decimal CostPerMinute);
