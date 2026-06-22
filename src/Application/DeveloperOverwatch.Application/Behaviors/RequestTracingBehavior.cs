namespace DeveloperOverwatch.Application.Behaviors;

using System.Diagnostics;
using MediatR;

/// <summary>
/// Wraps each request in an OpenTelemetry <see cref="Activity"/> span, giving end-to-end
/// timing, status, and exception recording for the whole MediatR pipeline in one trace.
/// Registered as the outermost behavior so the span covers every inner behavior and the handler.
/// </summary>
/// <remarks>
/// Spans are only produced when a listener subscribes to
/// <see cref="ApplicationTelemetry.ActivitySourceName"/> (the API registers it with Azure
/// Monitor when Application Insights is configured); otherwise this is a near-zero-cost no-op.
/// </remarks>
public sealed class RequestTracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private static readonly ActivitySource Source = new(ApplicationTelemetry.ActivitySourceName);

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestType = typeof(TRequest);
        using var activity = Source.StartActivity(requestType.Name);
        activity?.SetTag("mediatr.request_type", requestType.FullName);

        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message }
            }));
            throw;
        }
    }
}
