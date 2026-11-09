namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Queries.GetPricing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/pricing")]
[Authorize]
[Produces("application/json")]
[Tags("Pricing")]
public sealed class PricingController : ControllerBase
{
    private readonly IMediator _mediator;

    public PricingController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPricing(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPricingQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
