namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Enums;

/// <summary>Usage of the turns that share one UTC 15-minute slot, one session, and one model, subagent and branch.
/// Every current time-zone offset is a whole number of quarter hours, so a slot never straddles a local midnight
/// and can be placed on exactly one local day in any zone. Rows are fine enough that every
/// <see cref="UsageDimension"/> split is a regrouping of them.</summary>
/// <param name="SlotStartUtc">Start of the slot, UTC, on a quarter hour.</param>
/// <param name="SessionId">The session the turns belong to; subagent turns are stored under their parent session.</param>
/// <param name="Model">The turns' model; null when they carry none.</param>
/// <param name="SubagentName">The subagent that made the turns; null for the main thread.</param>
/// <param name="GitBranch">The branch the turns were made on; null when they carry none.</param>
/// <param name="Project">The session's project key, or its repository path when the Agent sent no key.</param>
/// <param name="DeveloperEmail">The session's developer.</param>
/// <param name="MachineName">The machine the session ran on; null when unknown.</param>
/// <param name="Agent">The coding agent that ran the session.</param>
/// <param name="FirstAtUtc">Earliest turn in the row.</param>
/// <param name="LastAtUtc">Latest turn in the row.</param>
public sealed record UsageSlotRow(
    DateTime SlotStartUtc,
    string SessionId,
    string? Model,
    string? SubagentName,
    string? GitBranch,
    string? Project,
    string DeveloperEmail,
    string? MachineName,
    string Agent,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd,
    int TurnCount,
    DateTime FirstAtUtc,
    DateTime LastAtUtc)
{
    /// <summary>This row's value for <paramref name="dimension"/>, the key its split groups on.</summary>
    public string? KeyOf(UsageDimension dimension) => dimension switch
    {
        UsageDimension.Model => Model,
        UsageDimension.Project => Project,
        UsageDimension.Session => SessionId,
        UsageDimension.Subagent => SubagentName,
        UsageDimension.Branch => GitBranch,
        UsageDimension.Developer => DeveloperEmail,
        UsageDimension.Machine => MachineName,
        UsageDimension.Agent => Agent,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
    };
}
