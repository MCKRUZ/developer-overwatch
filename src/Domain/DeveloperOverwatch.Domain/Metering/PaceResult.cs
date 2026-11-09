namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// The outcome of <see cref="Pace.Compute"/>: how much of the limit is used against how much of the period has elapsed.
/// </summary>
/// <param name="UsedFraction">Used divided by limit. Not clamped; above 1 means the limit is exceeded.</param>
/// <param name="ElapsedFraction">Share of the period elapsed, clamped to [0, 1].</param>
/// <param name="Status">Whether spending is ahead of, on, or behind the passage of time.</param>
public sealed record PaceResult(decimal UsedFraction, decimal ElapsedFraction, PaceStatus Status);
