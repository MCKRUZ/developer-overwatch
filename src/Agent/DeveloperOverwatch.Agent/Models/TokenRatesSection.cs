using DeveloperOverwatch.Domain.Models;

namespace DeveloperOverwatch.Agent.Models;

/// <summary>One set of USD-per-million-token rates in a pricing section. Same shape as the server's TokenRatesOptions.</summary>
public class TokenRatesSection
{
    public decimal InputPerMillionUsd { get; init; }
    public decimal OutputPerMillionUsd { get; init; }
    public decimal CacheWritePerMillionUsd { get; init; }
    public decimal CacheReadPerMillionUsd { get; init; }

    public TokenRates ToTokenRates() =>
        new(InputPerMillionUsd, OutputPerMillionUsd, CacheWritePerMillionUsd, CacheReadPerMillionUsd);
}
