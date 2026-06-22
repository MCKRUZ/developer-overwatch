namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Application.Idempotency;
using DeveloperOverwatch.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// Short-circuits duplicate requests to a cached response. Only requests implementing
/// <see cref="IIdempotentRequest"/> are affected; everything else passes through untouched.
/// </summary>
/// <remarks>
/// Registered as the outermost behavior so a duplicate returns the cached response before
/// validation and the handler run. Failure <see cref="Result{T}"/>s are deliberately not
/// cached, so a legitimate retry after a transient failure still re-executes the handler.
/// </remarks>
public sealed class IdempotencyBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IIdempotencyStore _store;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;

    public IdempotencyBehavior(
        IIdempotencyStore store,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotentRequest idempotent)
            return await next();

        var key = idempotent.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
            return await next();

        var cached = await _store.TryGetAsync(key, cancellationToken);
        if (cached is TResponse cachedResponse)
        {
            _logger.LogDebug("Idempotent hit for {Request} key '{Key}' — returning cached response",
                typeof(TRequest).Name, key);
            return cachedResponse;
        }

        var response = await next();

        if (response is IResult { IsSuccess: false })
        {
            _logger.LogDebug("Idempotent miss for {Request} key '{Key}' — failure not cached",
                typeof(TRequest).Name, key);
            return response;
        }

        await _store.SetAsync(key, response!, cancellationToken);
        return response;
    }
}
