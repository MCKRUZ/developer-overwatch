namespace DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Replaces a plan's name, price and window limit. The developer email is its identity and never changes.</summary>
public sealed record UpdateDeveloperPlanCommand(
    Guid Id,
    string PlanName,
    decimal MonthlyPriceUsd,
    decimal? WindowLimitUsd) : IRequest<Result<DeveloperPlanDto>>;
