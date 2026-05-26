namespace DeveloperOverwatch.API.Middleware;

public sealed class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _validKeys;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var keys = configuration.GetSection("ApiKeys:Keys").Get<string[]>() ?? [];
        _validKeys = new HashSet<string>(keys, StringComparer.Ordinal);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (_validKeys.Count == 0)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedKey) ||
            !_validKeys.Contains(extractedKey.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        await _next(context);
    }
}
