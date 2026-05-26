namespace DeveloperOverwatch.API.Middleware;

using System.Security.Cryptography;
using System.Text;

public sealed class WebhookSignatureMiddleware
{
    private const string SignatureHeader = "X-Hub-Signature-256";
    private readonly RequestDelegate _next;
    private readonly string? _secret;

    public WebhookSignatureMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _secret = configuration.GetValue<string>("GitHub:WebhookSecret");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_secret))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(SignatureHeader, out var signatureHeader) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing webhook signature" });
            return;
        }

        context.Request.EnableBuffering();
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var expectedSignature = ComputeSignature(body, _secret);
        var actualSignature = signatureHeader.ToString();

        if (actualSignature.StartsWith("sha256="))
            actualSignature = actualSignature["sha256=".Length..];

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(actualSignature)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid webhook signature" });
            return;
        }

        await _next(context);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
