namespace DeveloperOverwatch.Domain.Models;

/// <summary>One model's pricing: standard rates, plus an optional long-prompt tier.</summary>
public sealed record ModelRates(TokenRates Standard, LongPromptTier? LongPrompt = null)
{
    /// <param name="promptTokens">Input + cache write + cache read tokens of one request.</param>
    public TokenRates RatesForPrompt(long promptTokens) =>
        LongPrompt is not null && promptTokens > LongPrompt.ThresholdTokens ? LongPrompt.Rates : Standard;
}
