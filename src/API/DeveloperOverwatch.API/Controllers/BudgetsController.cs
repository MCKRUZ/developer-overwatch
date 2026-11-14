namespace DeveloperOverwatch.API.Controllers;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Application.Commands.CreateBudget;
using DeveloperOverwatch.Application.Commands.DeleteBudget;
using DeveloperOverwatch.Application.Commands.UpdateBudget;
using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetBudgets;
using DeveloperOverwatch.Application.Queries.GetBudgetStatuses;
using DeveloperOverwatch.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Budgets: anyone who can see the dashboard reads them; writes need <see cref="AuthPolicies.BudgetEdit"/>.
/// No [Produces("application/json")]: it would also stamp validation problems application/json instead of
/// application/problem+json.</summary>
[ApiController]
[Route("api/budgets")]
[Authorize(Policy = AuthPolicies.DashboardRead)]
[Tags("Budgets")]
public sealed class BudgetsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BudgetsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BudgetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBudgets(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBudgetsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    /// <summary>Every budget's spend in its current period: the viewer's local day or calendar month in
    /// <paramref name="tz"/>, which binds as nullable so a missing one reaches the validator.</summary>
    [HttpGet("status")]
    [ProducesResponseType<IReadOnlyList<BudgetStatusDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatuses([FromQuery] string? tz, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBudgetStatusesQuery(tz), ct);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType<BudgetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateBudgetCommand(request.Scope, request.DeveloperEmail, request.Period, request.AmountUsd), ct);
        return result.IsSuccess ? Created($"/api/budgets/{result.Value!.Id}", result.Value) : this.Failure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType<BudgetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBudget(Guid id, [FromBody] UpdateBudgetRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateBudgetCommand(id, request.AmountUsd), ct);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BudgetEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBudget(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteBudgetCommand(id), ct);
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // Required, so a body that leaves one out is a 400 rather than silently the enum's first value or 0.
    public sealed record CreateBudgetRequest(
        [property: JsonRequired] BudgetScope Scope,
        string? DeveloperEmail,
        [property: JsonRequired] BudgetPeriod Period,
        [property: JsonRequired] decimal AmountUsd);

    public sealed record UpdateBudgetRequest([property: JsonRequired] decimal AmountUsd);
}
