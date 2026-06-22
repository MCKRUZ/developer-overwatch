namespace DeveloperOverwatch.API.Tests.Middleware;

using DeveloperOverwatch.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

public class RequestLoggingMiddlewareTests
{
    private sealed class CapturingLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static async Task<CapturingLogger> RunAsync(string path, int statusCode)
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";

        var middleware = new RequestLoggingMiddleware(
            ctx => { ctx.Response.StatusCode = statusCode; return Task.CompletedTask; },
            logger);

        await middleware.InvokeAsync(context);
        return logger;
    }

    [Fact]
    public async Task InvokeAsync_HealthEndpoint_DoesNotLog()
    {
        var logger = await RunAsync("/health", 200);

        logger.Levels.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_HealthSubPath_DoesNotLog()
    {
        var logger = await RunAsync("/health/ready", 200);

        logger.Levels.Should().BeEmpty();
    }

    [Theory]
    [InlineData(200, LogLevel.Information)]
    [InlineData(204, LogLevel.Information)]
    [InlineData(302, LogLevel.Information)]
    [InlineData(400, LogLevel.Warning)]
    [InlineData(404, LogLevel.Warning)]
    [InlineData(499, LogLevel.Warning)]
    [InlineData(500, LogLevel.Error)]
    [InlineData(503, LogLevel.Error)]
    public async Task InvokeAsync_LogsLevelBasedOnStatusCode(int statusCode, LogLevel expected)
    {
        var logger = await RunAsync("/api/sessions", statusCode);

        logger.Levels.Should().ContainSingle().Which.Should().Be(expected);
    }
}
