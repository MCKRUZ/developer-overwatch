namespace DeveloperOverwatch.Application.Queries.GetTeamAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetTeamAnalyticsQuery : IRequest<Result<TeamAnalytics>>;
