using DeveloperOverwatch.Domain.Models;

namespace DeveloperOverwatch.Agent.Models;

/// <summary>One model's rates with an optional long-prompt tier. Same shape and rules as the server's ModelRatesOptions.</summary>
public sealed class ModelRatesSection : TokenRatesSection
{
    public long? LongPromptThresholdTokens { get; init; }
    public TokenRatesSection? LongPrompt { get; init; }

    /// <exception cref="InvalidOperationException">Only one of the threshold and the tier is set.</exception>
    public ModelRates ToModelRates(string model)
    {
        if (LongPromptThresholdTokens.HasValue != (LongPrompt is not null))
            throw new InvalidOperationException(
                $"Model '{model}' must set both LongPromptThresholdTokens and LongPrompt, or neither.");

        var tier = LongPrompt is null
            ? null
            : new LongPromptTier(LongPromptThresholdTokens!.Value, LongPrompt.ToTokenRates());
        return new ModelRates(ToTokenRates(), tier);
    }
}
