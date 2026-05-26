namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.ResolveViolation;
using DeveloperOverwatch.Application.Queries.GetViolations;
using DeveloperOverwatch.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/governance")]
public class GovernanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public GovernanceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("violations/{sessionId}")]
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
        return result.IsSuccess ? Ok(new { violationId = result.Value }) : BadRequest(result.Error);
    }

    public sealed record ResolveViolationRequest(string ResolvedBy, string ResolutionReason);
}
