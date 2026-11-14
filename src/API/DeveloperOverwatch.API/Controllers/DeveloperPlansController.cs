namespace DeveloperOverwatch.API.Controllers;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;
using DeveloperOverwatch.Application.Commands.DeleteDeveloperPlan;
using DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;
using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetDeveloperPlans;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Developer plans: anyone who can see the dashboard reads them; writes need
/// <see cref="AuthPolicies.BudgetEdit"/>. No [Produces("application/json")]: it would also stamp validation problems
/// application/json instead of application/problem+json.</summary>
[ApiController]
[Route("api/developer-plans")]
[Authorize(Policy = AuthPolicies.DashboardRead)]
[Tags("Developer plans")]
public sealed class DeveloperPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeveloperPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeveloperPlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeveloperPlans(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDeveloperPlansQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType<DeveloperPlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDeveloperPlan(
        [FromBody] CreateDeveloperPlanRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateDeveloperPlanCommand(
            request.DeveloperEmail, request.PlanName, request.MonthlyPriceUsd, request.WindowLimitUsd), ct);
        return result.IsSuccess
            ? Created($"/api/developer-plans/{result.Value!.Id}", result.Value)
            : this.Failure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType<DeveloperPlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDeveloperPlan(
        Guid id, [FromBody] UpdateDeveloperPlanRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateDeveloperPlanCommand(
            id, request.PlanName, request.MonthlyPriceUsd, request.WindowLimitUsd), ct);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDeveloperPlan(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteDeveloperPlanCommand(id), ct);
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // The price is required, so a body that leaves it out is a 400 rather than silently 0.
    public sealed record CreateDeveloperPlanRequest(
        string DeveloperEmail,
        string PlanName,
        [property: JsonRequired] decimal MonthlyPriceUsd,
        decimal? WindowLimitUsd);

    public sealed record UpdateDeveloperPlanRequest(
        string PlanName,
        [property: JsonRequired] decimal MonthlyPriceUsd,
        decimal? WindowLimitUsd);
}
