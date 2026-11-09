namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Models;

/// <summary>Binding shape for one set of USD-per-million-token rates.</summary>
public class TokenRatesOptions
{
    public decimal InputPerMillionUsd { get; set; }
    public decimal OutputPerMillionUsd { get; set; }
    public decimal CacheWritePerMillionUsd { get; set; }
    public decimal CacheReadPerMillionUsd { get; set; }

    public TokenRates ToTokenRates() =>
        new(InputPerMillionUsd, OutputPerMillionUsd, CacheWritePerMillionUsd, CacheReadPerMillionUsd);
}
