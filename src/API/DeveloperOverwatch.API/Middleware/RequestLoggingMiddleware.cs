namespace DeveloperOverwatch.API.Middleware;

using System.Diagnostics;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();

        var statusCode = context.Response.StatusCode;
        var method = context.Request.Method;
        var path = context.Request.Path;
        var duration = stopwatch.ElapsedMilliseconds;

        if (statusCode >= 500)
        {
            _logger.LogError(
                "HTTP {Method} {Path} responded {StatusCode} in {Duration}ms",
                method, path, statusCode, duration);
        }
        else if (statusCode >= 400)
        {
            _logger.LogWarning(
                "HTTP {Method} {Path} responded {StatusCode} in {Duration}ms",
                method, path, statusCode, duration);
        }
        else
        {
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {Duration}ms",
                method, path, statusCode, duration);
        }
    }
}
