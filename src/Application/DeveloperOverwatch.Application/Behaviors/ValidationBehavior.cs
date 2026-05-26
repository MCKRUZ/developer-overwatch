namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validators = _validators as IValidator<TRequest>[] ?? _validators.ToArray();
        if (validators.Length == 0)
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ToList();

        if (errors.Count == 0)
            return await next();

        _logger.LogDebug("Validation failed for {Request} with {Count} errors",
            typeof(TRequest).Name, errors.Count);

        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var resultType = typeof(TResponse);
            var method = resultType.GetMethod("ValidationFailure")!;
            return (TResponse)method.Invoke(null, [errors.AsReadOnly()])!;
        }

        throw new ValidationException(results.SelectMany(r => r.Errors));
    }
}
