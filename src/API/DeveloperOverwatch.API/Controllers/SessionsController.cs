namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Application.Queries.GetSession;
using DeveloperOverwatch.Application.Queries.GetSessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Route("api/sessions")]
[Authorize]
[Produces("application/json")]
[Tags("Sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<OverwatchHub, IOverwatchHubClient> _hub;

    public SessionsController(
        IMediator mediator,
        IHubContext<OverwatchHub, IOverwatchHubClient> hub)
    {
        _mediator = mediator;
        _hub = hub;
    }

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

        if (!result.IsSuccess)
        {
            return result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : Conflict(new { error = result.Error });
        }

        await _hub.Clients.All.SessionStarted(
            command.SessionId, command.DeveloperEmail, command.RepoPath);

        return Ok(new { sessionId = result.Value });
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

        if (!result.IsSuccess)
        {
            return result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
        }

        await _hub.Clients.Group(sessionId).EventsIngested(
            sessionId,
            command.ConversationTurns.Count,
            command.ToolExecutions.Count,
            command.FileChanges.Count);

        await _hub.Clients.All.EventsIngested(
            sessionId,
            command.ConversationTurns.Count,
            command.ToolExecutions.Count,
            command.FileChanges.Count);

        return Ok(new { eventsIngested = result.Value });
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

        if (!result.IsSuccess)
        {
            return result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
        }

        await _hub.Clients.Group(sessionId).SessionEnded(sessionId);
        await _hub.Clients.All.SessionEnded(sessionId);

        return Ok(new { sessionId = result.Value });
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
