namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Models;

/// <summary>Binding shape for the "ModelContextWindows" section; no in-code sizes, for the same reason as <see cref="TokenPricingOptions"/>.</summary>
public sealed class ModelContextWindowsOptions
{
    public DateOnly AsOf { get; set; }
    public Dictionary<string, long> Models { get; set; } = [];

    public ModelContextWindowTable ToTable()
    {
        if (AsOf == default)
            throw new InvalidOperationException("ModelContextWindows:AsOf must be set to the date the sizes were published.");

        return new ModelContextWindowTable(AsOf, Models);
    }
}
