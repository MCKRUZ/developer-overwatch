namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.AddAnnotation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/annotations")]
[Authorize]
public sealed class AnnotationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnnotationsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> AddAnnotation([FromBody] AddAnnotationRequest request, CancellationToken ct)
    {
        var command = new AddAnnotationCommand(
            request.SessionId,
            request.Content,
            request.CreatedBy,
            request.TurnId,
            request.ToolExecutionId);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
            return Created($"/api/sessions/{request.SessionId}", new { annotationId = result.Value });

        if (result.ValidationErrors?.Count > 0)
            return BadRequest(result.ValidationErrors);

        return result.Error?.Contains("not found") == true
            ? NotFound(result.Error)
            : BadRequest(result.Error);
    }

    public sealed record AddAnnotationRequest(
        string SessionId,
        string Content,
        string CreatedBy,
        string? TurnId = null,
        string? ToolExecutionId = null);
}
