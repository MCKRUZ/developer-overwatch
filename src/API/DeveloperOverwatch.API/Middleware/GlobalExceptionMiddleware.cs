namespace DeveloperOverwatch.API.Middleware;

using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Catches unhandled exceptions and converts them to RFC 7807 ProblemDetails responses.
/// Register as the outermost middleware so every downstream failure is mapped consistently.
/// </summary>
/// <remarks>
/// This codebase returns <c>Result&lt;T&gt;</c> for expected failures, so a thrown exception is
/// genuinely exceptional. Production responses carry a generic message (no stack traces or
/// internal paths leak); Development responses include the exception detail for debugging.
/// <see cref="ValidationException"/> is the one mapped special case: the MediatR
/// <c>ValidationBehavior</c> throws it for non-<c>Result</c> responses, and it represents a
/// client error (400), not a server fault.
/// </remarks>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteValidationProblemAsync(context, ex);
        }
        catch (Exception ex)
        {
            await WriteServerErrorAsync(context, ex);
        }
    }

    private async Task WriteValidationProblemAsync(HttpContext context, ValidationException ex)
    {
        _logger.LogDebug("Validation failure on {Method} {Path}: {Count} error(s)",
            context.Request.Method, context.Request.Path, ex.Errors.Count());

        var errors = ex.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = context.Request.Path
        };

        await WriteProblemAsync(context, problem);
    }

    private async Task WriteServerErrorAsync(HttpContext context, Exception ex)
    {
        _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Instance = context.Request.Path,
            Detail = _env.IsDevelopment() ? ex.ToString() : null
        };

        await WriteProblemAsync(context, problem);
    }

    private static async Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        // Serialize with the runtime type so a ValidationProblemDetails keeps its Errors —
        // System.Text.Json would otherwise serialize only the declared ProblemDetails members.
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, problem.GetType(), ProblemJsonOptions));
    }

    private static readonly JsonSerializerOptions ProblemJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
