namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Usage of a period of local days in the viewer's time zone, rolled into day, week or month buckets
/// and split by model, project, session, subagent, branch, developer, machine and agent.</summary>
/// <param name="From">First local day, inclusive.</param>
/// <param name="To">Last local day, inclusive.</param>
/// <param name="Granularity">"day", "week" (ISO, starting Monday) or "month".</param>
/// <param name="TimeZone">IANA zone the days are counted in, e.g. "America/New_York".</param>
/// <param name="Developer">Only this developer's sessions, when set.</param>
/// <param name="Project">Only this project (project key, or repository path when there is no key), when set.</param>
public sealed record GetUsageSummaryQuery(
    DateOnly? From,
    DateOnly? To,
    string? Granularity,
    string? TimeZone,
    string? Developer = null,
    string? Project = null) : IRequest<Result<UsageSummary>>;
