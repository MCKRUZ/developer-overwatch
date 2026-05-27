namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;
using DeveloperOverwatch.Application.Queries.GetTeamAnalytics;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/analytics")]
[Authorize]
[Produces("application/json")]
[Tags("Analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("team")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTeamAnalytics(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTeamAnalyticsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("developer/{email}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeveloperAnalytics(string email, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDeveloperAnalyticsQuery(email), ct);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Contains("not found") == true
            ? NotFound(result.Error)
            : BadRequest(result.Error);
    }
}
