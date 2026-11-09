namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// One observation of usage at a moment, the input to <see cref="BurnRate.Over"/>.
/// </summary>
/// <param name="AtUtc">UTC moment the usage happened.</param>
/// <param name="Tokens">Tokens consumed in this observation.</param>
/// <param name="CostUsd">Cost of this observation in US dollars.</param>
public sealed record UsagePoint(DateTime AtUtc, long Tokens, decimal CostUsd);
