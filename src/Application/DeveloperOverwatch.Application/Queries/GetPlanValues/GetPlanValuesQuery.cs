namespace DeveloperOverwatch.Application.Queries.GetPlanValues;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Every developer plan's price against the API-equivalent value of that developer's usage, month to date
/// in the viewer's local month, by email.</summary>
/// <param name="TimeZone">IANA zone the month is counted in, e.g. "America/New_York" (query parameter <c>tz</c>).</param>
public sealed record GetPlanValuesQuery(string? TimeZone) : IRequest<Result<IReadOnlyList<PlanValueDto>>>;
