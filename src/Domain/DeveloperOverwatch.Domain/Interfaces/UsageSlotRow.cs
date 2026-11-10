namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>Usage of the turns that share one UTC 15-minute slot and one dimension key. Every current time-zone
/// offset is a whole number of quarter hours, so a slot never straddles a local midnight and can be placed on
/// exactly one local day in any zone.</summary>
/// <param name="SlotStartUtc">Start of the slot, UTC, on a quarter hour.</param>
/// <param name="Key">The dimension's value for these turns; null when the turns carry none.</param>
/// <param name="FirstAtUtc">Earliest turn in the slot.</param>
/// <param name="LastAtUtc">Latest turn in the slot.</param>
public sealed record UsageSlotRow(
    DateTime SlotStartUtc,
    string? Key,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd,
    int TurnCount,
    DateTime FirstAtUtc,
    DateTime LastAtUtc);
