namespace DeveloperOverwatch.Domain.Models;

using DeveloperOverwatch.Domain.Interfaces;

/// <summary>Context-window sizes in tokens as published on <see cref="AsOf"/>, resolved per model id.</summary>
public sealed class ModelContextWindowTable : IModelContextWindowProvider
{
    private readonly ModelKeyLookup<long> _windows;

    /// <exception cref="ArgumentException">No "default" entry, or two keys differ only by case.</exception>
    public ModelContextWindowTable(DateOnly asOf, IEnumerable<KeyValuePair<string, long>> models)
    {
        AsOf = asOf;
        _windows = new ModelKeyLookup<long>(models);
    }

    public DateOnly AsOf { get; }

    public long ContextWindowTokensFor(string? model) => _windows.Resolve(model);
}
