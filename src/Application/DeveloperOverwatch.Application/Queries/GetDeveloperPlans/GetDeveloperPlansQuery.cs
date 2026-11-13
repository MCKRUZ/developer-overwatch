namespace DeveloperOverwatch.Application.Queries.GetDeveloperPlans;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Every developer plan, by email.</summary>
public sealed record GetDeveloperPlansQuery : IRequest<Result<IReadOnlyList<DeveloperPlanDto>>>;
