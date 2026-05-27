namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetDeveloperAnalyticsQuery(string Email) : IRequest<Result<DeveloperAnalytics>>;
