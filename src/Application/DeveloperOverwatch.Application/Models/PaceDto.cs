namespace DeveloperOverwatch.Application.Models;

using DeveloperOverwatch.Domain.Metering;

/// <summary>Share of a limit used against share of its period elapsed, as <see cref="Pace.Compute"/> found it.</summary>
public sealed record PaceDto(decimal UsedFraction, decimal ElapsedFraction, PaceStatus Status)
{
    public static PaceDto From(PaceResult pace) => new(pace.UsedFraction, pace.ElapsedFraction, pace.Status);
}
