namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Application.Queries.GetSession;
using DeveloperOverwatch.Application.Queries.GetSessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/sessions")]
[Authorize]
[Produces("application/json")]
[Tags("Sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("start")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartSession(
        [FromBody] IngestSessionStartCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { sessionId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : Conflict(new { error = result.Error });
    }

    [HttpPost("{sessionId}/events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestEvents(
        string sessionId,
        [FromBody] IngestSessionEventsCommand command,
        CancellationToken ct)
    {
        if (command.SessionId != sessionId)
            return BadRequest(new { error = "Session ID mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { eventsIngested = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }

    [HttpPost("{sessionId}/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSession(
        string sessionId,
        [FromBody] IngestSessionEndCommand command,
        CancellationToken ct)
    {
        if (command.SessionId != sessionId)
            return BadRequest(new { error = "Session ID mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { sessionId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }

    [HttpGet("{sessionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSessionQuery(sessionId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessions(
        [FromQuery] string? developer = null,
        [FromQuery] string? repo = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSessionsQuery(developer, repo), ct);
        return Ok(result.Value);
    }
}
