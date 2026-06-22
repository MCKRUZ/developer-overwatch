namespace DeveloperOverwatch.Application.Idempotency;

/// <summary>
/// Caches handler responses by idempotency key so duplicate requests skip re-execution.
/// The default implementation is in-process; replace it with a distributed store
/// (Redis, database) when running multiple replicas.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns the cached response for <paramref name="idempotencyKey"/>, or null on a miss.</summary>
    Task<object?> TryGetAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Caches <paramref name="response"/> under <paramref name="idempotencyKey"/>. TTL is implementation-defined.</summary>
    Task SetAsync(string idempotencyKey, object response, CancellationToken cancellationToken);
}
