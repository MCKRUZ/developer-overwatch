namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.ReviewPullRequest;
using DeveloperOverwatch.Application.Queries.GetPullRequestDetail;
using DeveloperOverwatch.Application.Queries.GetPullRequests;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/prs")]
[Authorize]
[Produces("application/json")]
[Tags("Pull Requests")]
public sealed class PullRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PullRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPullRequests(
        [FromQuery] string? repo = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPullRequestsQuery(repo), ct);
        return Ok(result.Value);
    }

    [HttpGet("{owner}/{repoName}/{prNumber:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPullRequestDetail(
        string owner, string repoName, int prNumber, CancellationToken ct)
    {
        var repo = $"{owner}/{repoName}";
        var result = await _mediator.Send(new GetPullRequestDetailQuery(repo, prNumber), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    [HttpPost("{owner}/{repoName}/{prNumber:int}/review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReviewPullRequest(
        string owner,
        string repoName,
        int prNumber,
        [FromBody] ReviewPullRequestCommand command,
        CancellationToken ct)
    {
        var repo = $"{owner}/{repoName}";
        if (command.Repo != repo || command.PrNumber != prNumber)
            return BadRequest(new { error = "Route and body mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { reviewId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }
}
