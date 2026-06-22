namespace DeveloperOverwatch.API.Tests.Middleware;

using DeveloperOverwatch.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpContext> RunAsync(bool isHttps)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = isHttps ? "https" : "http";

        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        nextCalled.Should().BeTrue("the middleware must always call the next delegate");
        return context;
    }

    [Fact]
    public async Task InvokeAsync_SetsAllStaticSecurityHeaders()
    {
        var context = await RunAsync(isHttps: false);
        var headers = context.Response.Headers;

        headers["X-Frame-Options"].ToString().Should().Be("DENY");
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-XSS-Protection"].ToString().Should().Be("0");
        headers["Content-Security-Policy"].ToString().Should().Be("default-src 'none'; frame-ancestors 'none'");
        headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        headers["Permissions-Policy"].ToString().Should().Be("camera=(), microphone=(), geolocation=()");
    }

    [Fact]
    public async Task InvokeAsync_Https_SetsHstsHeader()
    {
        var context = await RunAsync(isHttps: true);

        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task InvokeAsync_Http_DoesNotSetHstsHeader()
    {
        var context = await RunAsync(isHttps: false);

        context.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
    }
}
