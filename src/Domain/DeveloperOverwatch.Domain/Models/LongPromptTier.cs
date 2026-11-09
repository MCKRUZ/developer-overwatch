namespace DeveloperOverwatch.Domain.Models;

/// <summary>
/// Alternate rates a model charges once a request's prompt exceeds <paramref name="ThresholdTokens"/>.
/// They replace every category, output included.
/// </summary>
public sealed record LongPromptTier(long ThresholdTokens, TokenRates Rates);
