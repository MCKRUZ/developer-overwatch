namespace DeveloperOverwatch.Application.Queries.GetTeamAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetTeamAnalyticsQuery(
    DateTime? From = null, DateTime? To = null, string? Developer = null) : IRequest<Result<TeamAnalytics>>;
