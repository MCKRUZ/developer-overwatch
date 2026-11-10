namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Queries.GetRunningSessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Usage meter reads. Plain <c>[Authorize]</c> uses the Smart scheme, so the dashboard reaches it
/// anonymously when OIDC is not configured and with a JWT when it is.</summary>
[ApiController]
[Route("api/usage")]
[Authorize]
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
    public async Task<IActionResult> GetRunning(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRunningSessionsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
