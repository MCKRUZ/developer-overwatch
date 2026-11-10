namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Queries.GetRunningSessions;
using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Usage meter reads. The DashboardRead policy lets the dashboard in anonymously when OIDC is not
/// configured and with a JWT when it is, and never accepts the agent API key.</summary>
[ApiController]
[Route("api/usage")]
[Authorize(Policy = AuthPolicies.DashboardRead)]
[Produces("application/json")]
[Tags("Usage")]
public sealed class UsageController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsageController(IMediator mediator) => _mediator = mediator;

    /// <summary>Sessions running right now, with usage so far and burn over the last 15 minutes.</summary>
    [HttpGet("running")]
    [ProducesResponseType(typeof(IReadOnlyList<RunningSessionItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRunning(
        [FromQuery] string? developer, [FromQuery] string? project, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRunningSessionsQuery(developer, project), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Usage of the local days <paramref name="from"/>..<paramref name="to"/> (inclusive) in
    /// <paramref name="timeZone"/>, in day, week or month buckets, with splits and burn.</summary>
    /// <remarks>Every parameter binds as nullable so a missing one reaches the validator and gets its own
    /// message, instead of defaulting to 0001-01-01 or being refused by model binding.</remarks>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(UsageSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? granularity,
        [FromQuery] string? timeZone,
        [FromQuery] string? developer,
        [FromQuery] string? project,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetUsageSummaryQuery(from, to, granularity, timeZone, developer, project), ct);
        if (result.IsSuccess)
            return Ok(result.Value);
        return result.ValidationErrors.Count > 0
            ? BadRequest(new { errors = result.ValidationErrors })
            : BadRequest(result.Error);
    }
}
