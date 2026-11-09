namespace DeveloperOverwatch.Domain.Models;

/// <summary>
/// Per-model values keyed by model id or id prefix, matched case-insensitively. A model id resolves
/// to its exact key, else the longest key it starts with at a version boundary, else
/// <see cref="DefaultKey"/>. Longest-prefix matching lets "claude-opus-5-5" and "claude-opus-5"
/// carry different values while dated ids ("claude-opus-5-20260101") still find their version.
/// </summary>
public sealed class ModelKeyLookup<T>
{
    public const string DefaultKey = "default";

    private readonly Dictionary<string, T> _entries;
    private readonly T _default;

    /// <exception cref="ArgumentException">No "default" entry, or two keys differ only by case.</exception>
    public ModelKeyLookup(IEnumerable<KeyValuePair<string, T>> entries)
    {
        _entries = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
            _entries.Add(key, value);

        if (!_entries.TryGetValue(DefaultKey, out var fallback))
            throw new ArgumentException($"Model table must include a '{DefaultKey}' entry.", nameof(entries));
        _default = fallback;
    }

    public T Resolve(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return _default;
        if (_entries.TryGetValue(model, out var exact))
            return exact;

        var bestLength = 0;
        var best = _default;
        foreach (var (key, value) in _entries)
        {
            if (key.Length > bestLength && IsVersionPrefix(key, model))
            {
                bestLength = key.Length;
                best = value;
            }
        }

        return best;
    }

    // "claude-opus-4-1" must not claim "claude-opus-4-10": the key has to end where a version
    // segment ends (end of id, or a non-alphanumeric such as '-', '@', '[').
    private static bool IsVersionPrefix(string key, string model) =>
        !string.Equals(key, DefaultKey, StringComparison.OrdinalIgnoreCase)
        && model.Length > key.Length
        && model.StartsWith(key, StringComparison.OrdinalIgnoreCase)
        && !char.IsLetterOrDigit(model[key.Length]);
}
