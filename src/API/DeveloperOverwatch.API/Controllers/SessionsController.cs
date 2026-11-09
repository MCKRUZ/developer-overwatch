namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Application.Queries.GetSession;
using DeveloperOverwatch.Application.Queries.GetSessionDailyUsage;
using DeveloperOverwatch.Application.Queries.GetSessionModelBreakdown;
using DeveloperOverwatch.Application.Queries.GetSessionTimelinePage;
using DeveloperOverwatch.Application.Queries.GetSessionTimelinePageAround;
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
    [Authorize(Policy = AuthPolicies.Agent)]
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
    [Authorize(Policy = AuthPolicies.Agent)]
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
                : SessionNotFound(result.Error);
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
    [Authorize(Policy = AuthPolicies.Agent)]
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
                : SessionNotFound(result.Error);
        }

        await _hub.Clients.Group(sessionId).SessionEnded(sessionId);
        await _hub.Clients.All.SessionEnded(sessionId);

        return Ok(new { sessionId = result.Value });
    }

    /// <summary>The events and end handlers fail only when the session does not exist. The Agent
    /// drops a session's queued events only on a 404 carrying this code, so a 404 from anything else
    /// (a wrong base path, a proxy) stays retryable.</summary>
    private NotFoundObjectResult SessionNotFound(string? error) =>
        NotFound(new { error, code = "session_not_found" });

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
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] DeveloperOverwatch.Domain.Enums.SessionStatus? status = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSessionsQuery(developer, repo, from, to, status), ct);
        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/timeline")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessionTimeline(
        string sessionId,
        [FromQuery] DateTime? before = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSessionTimelinePageQuery(sessionId, before, limit), ct);
        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/timeline/around")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessionTimelineAround(
        string sessionId,
        [FromQuery] string? turnId = null,
        [FromQuery] string? toolExecutionId = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetSessionTimelinePageAroundQuery(sessionId, turnId, toolExecutionId, limit), ct);

        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/daily-usage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessionDailyUsage(string sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSessionDailyUsageQuery(sessionId), ct);
        return Ok(result.Value);
    }

    [HttpGet("{sessionId}/model-breakdown")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessionModelBreakdown(string sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSessionModelBreakdownQuery(sessionId), ct);
        return Ok(result.Value);
    }
}
