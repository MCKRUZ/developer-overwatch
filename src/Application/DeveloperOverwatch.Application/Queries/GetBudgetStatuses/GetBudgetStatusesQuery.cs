namespace DeveloperOverwatch.Application.Queries.GetBudgetStatuses;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Every budget's spend in its current period, in the order of <c>GET /api/budgets</c>. Daily and monthly
/// periods are the viewer's local day and calendar month.</summary>
/// <param name="TimeZone">IANA zone the periods are counted in, e.g. "America/New_York" (query parameter <c>tz</c>).</param>
public sealed record GetBudgetStatusesQuery(string? TimeZone) : IRequest<Result<IReadOnlyList<BudgetStatusDto>>>;
