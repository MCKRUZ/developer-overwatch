namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

/// <summary>Collects one outcome per item id while a batch is processed. Items are validated first,
/// so an invalid item is rejected again on resend rather than turning into a duplicate.</summary>
internal sealed class IngestItemResultsBuilder
{
    private readonly List<string> _accepted = [];
    private readonly List<string> _duplicate = [];
    private readonly List<RejectedItem> _rejected = [];
    private readonly HashSet<string> _rejectedIds = new(StringComparer.Ordinal);

    public IReadOnlySet<string> RejectedIds => _rejectedIds;

    /// <summary>Reports invalid items as rejected and returns the valid ones, each id once.</summary>
    public List<T> Valid<T>(IEnumerable<T> items, Func<T, string> idOf, Func<T, string?> reasonRejected)
    {
        var valid = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idOf(item) ?? string.Empty;
            if (reasonRejected(item) is { } reason)
            {
                if (_rejectedIds.Add(id))
                    _rejected.Add(new RejectedItem(id, reason));
            }
            else if (seen.Add(id))
            {
                valid.Add(item);
            }
        }
        return valid;
    }

    /// <summary>Reports already-stored items as duplicate and returns the rest, as accepted.</summary>
    public List<T> New<T>(IEnumerable<T> valid, Func<T, string> idOf, IReadOnlySet<string> existingIds)
    {
        var fresh = new List<T>();
        foreach (var item in valid)
        {
            var id = idOf(item);
            if (existingIds.Contains(id))
            {
                _duplicate.Add(id);
            }
            else
            {
                _accepted.Add(id);
                fresh.Add(item);
            }
        }
        return fresh;
    }

    public IngestItemResults Build() => new([.. _accepted], [.. _duplicate], [.. _rejected]);
}
