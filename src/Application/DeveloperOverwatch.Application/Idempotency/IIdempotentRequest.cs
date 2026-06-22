namespace DeveloperOverwatch.Application.Idempotency;

/// <summary>
/// Marker for MediatR requests that must be safe to retry without re-running side effects.
/// The <see cref="Behaviors.IdempotencyBehavior{TRequest, TResponse}"/> uses
/// <see cref="IdempotencyKey"/> to return the cached response for a duplicate request.
/// </summary>
/// <remarks>
/// The caller supplies a stable, unique key per logical operation — e.g. a GitHub
/// <c>X-GitHub-Delivery</c> id, which is reused across the provider's retry deliveries.
/// </remarks>
public interface IIdempotentRequest
{
    /// <summary>A stable key identifying this logical operation. Duplicates share a key.</summary>
    string IdempotencyKey { get; }
}
