namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>One turn's usage and the developer whose session it belongs to.</summary>
/// <param name="AtUtc">UTC moment of the turn.</param>
/// <param name="Tokens">All four token kinds together.</param>
/// <param name="CostUsd">Estimated cost of the turn in US dollars.</param>
public sealed record DeveloperTurnPoint(string DeveloperEmail, DateTime AtUtc, long Tokens, decimal CostUsd);
