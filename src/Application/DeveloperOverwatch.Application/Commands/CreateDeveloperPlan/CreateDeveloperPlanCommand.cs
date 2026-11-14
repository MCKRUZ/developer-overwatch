namespace DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <param name="WindowLimitUsd">API-equivalent USD one 5-hour window allows; null for no limit.</param>
public sealed record CreateDeveloperPlanCommand(
    string DeveloperEmail,
    string PlanName,
    decimal MonthlyPriceUsd,
    decimal? WindowLimitUsd) : IRequest<Result<DeveloperPlanDto>>;
