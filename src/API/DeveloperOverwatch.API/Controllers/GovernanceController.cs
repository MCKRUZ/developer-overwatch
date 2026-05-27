namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.Application.Commands.ResolveViolation;
using DeveloperOverwatch.Application.Queries.GetViolations;
using DeveloperOverwatch.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Route("api/governance")]
[Authorize]
[Produces("application/json")]
[Tags("Governance")]
public sealed class GovernanceController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<OverwatchHub, IOverwatchHubClient> _hub;

    public GovernanceController(
        IMediator mediator,
        IHubContext<OverwatchHub, IOverwatchHubClient> hub)
    {
        _mediator = mediator;
        _hub = hub;
    }

    [HttpGet("violations/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetViolations(
        string sessionId,
        [FromQuery] ViolationSeverity? severity = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetViolationsQuery { SessionId = sessionId, Severity = severity }, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("violations/{violationId}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveViolation(
        string violationId,
        [FromBody] ResolveViolationRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResolveViolationCommand
        {
            ViolationId = violationId,
            ResolvedBy = request.ResolvedBy,
            ResolutionReason = request.ResolutionReason
        }, ct);

        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                return NotFound(result.Error);

            return BadRequest(result.Error);
        }

        await _hub.Clients.All.ViolationResolved(result.Value!);

        return Ok(new { violationId = result.Value });
    }

    public sealed record ResolveViolationRequest(string ResolvedBy, string ResolutionReason);
}
