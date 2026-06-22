namespace DeveloperOverwatch.Application.Idempotency;

using System.Collections.Concurrent;

/// <summary>
/// In-process, TTL-bounded <see cref="IIdempotencyStore"/>. Caches responses by reference so
/// the exact runtime type survives retrieval (no serialization round-trip), which
/// <see cref="Behaviors.IdempotencyBehavior{TRequest, TResponse}"/> relies on for its
/// <c>cached is TResponse</c> check.
/// </summary>
/// <remarks>
/// Single-process only: entries do not survive a restart and are not shared across instances.
/// Expired entries are evicted lazily on access — there is no background sweep.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryIdempotencyStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<object?> TryGetAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(idempotencyKey, out var entry))
            return Task.FromResult<object?>(null);

        if (_timeProvider.GetUtcNow() >= entry.ExpiresAt)
        {
            // Compare-and-remove so we don't clobber a fresh write that raced in after our read.
            ((ICollection<KeyValuePair<string, CacheEntry>>)_entries).Remove(
                new KeyValuePair<string, CacheEntry>(idempotencyKey, entry));
            return Task.FromResult<object?>(null);
        }

        return Task.FromResult<object?>(entry.Response);
    }

    public Task SetAsync(string idempotencyKey, object response, CancellationToken cancellationToken)
    {
        var expiresAt = _timeProvider.GetUtcNow() + DefaultTtl;
        _entries[idempotencyKey] = new CacheEntry(response, expiresAt);
        return Task.CompletedTask;
    }

    private readonly record struct CacheEntry(object Response, DateTimeOffset ExpiresAt);
}
