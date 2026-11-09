namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Models;

/// <summary>Binding shape for one model's rates, with an optional long-prompt tier.</summary>
public sealed class ModelRatesOptions : TokenRatesOptions
{
    public long? LongPromptThresholdTokens { get; set; }
    public TokenRatesOptions? LongPrompt { get; set; }

    public ModelRates ToModelRates(string model)
    {
        if (LongPromptThresholdTokens.HasValue != (LongPrompt is not null))
            throw new InvalidOperationException(
                $"TokenPricing model '{model}' must set both LongPromptThresholdTokens and LongPrompt, or neither.");

        var tier = LongPrompt is null
            ? null
            : new LongPromptTier(LongPromptThresholdTokens!.Value, LongPrompt.ToTokenRates());
        return new ModelRates(ToTokenRates(), tier);
    }
}
