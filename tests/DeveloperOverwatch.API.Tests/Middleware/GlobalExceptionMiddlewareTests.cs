namespace DeveloperOverwatch.API.Tests.Middleware;

using DeveloperOverwatch.API.Middleware;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class GlobalExceptionMiddlewareTests
{
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<(int Status, string Body, string? ContentType)> RunAsync(
        RequestDelegate next, IHostEnvironment env)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionMiddleware(next, env, NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync(), context.Response.ContentType);
    }

    [Fact]
    public async Task NoException_LeavesResponseUntouched()
    {
        var (status, body, _) = await RunAsync(
            async ctx => { ctx.Response.StatusCode = 200; await ctx.Response.WriteAsync("ok"); },
            new FakeEnvironment());

        status.Should().Be(200);
        body.Should().Be("ok");
    }

    [Fact]
    public async Task ValidationException_MapsTo400ProblemJson()
    {
        var ex = new ValidationException([new ValidationFailure("Email", "Email is required")]);

        var (status, body, contentType) = await RunAsync(_ => throw ex, new FakeEnvironment());

        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith("application/problem+json");
        body.Should().Contain("Email");
        body.Should().Contain("Email is required");
    }

    [Fact]
    public async Task UnhandledException_InProduction_ReturnsGenericProblem_NoStackTrace()
    {
        var (status, body, contentType) = await RunAsync(
            _ => throw new InvalidOperationException("secret internal detail"),
            new FakeEnvironment { EnvironmentName = Environments.Production });

        status.Should().Be(StatusCodes.Status500InternalServerError);
        contentType.Should().StartWith("application/problem+json");
        body.Should().NotContain("secret internal detail");
        body.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task UnhandledException_InDevelopment_IncludesDetail()
    {
        var (status, body, _) = await RunAsync(
            _ => throw new InvalidOperationException("debuggable detail"),
            new FakeEnvironment { EnvironmentName = Environments.Development });

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().Contain("debuggable detail");
    }
}
