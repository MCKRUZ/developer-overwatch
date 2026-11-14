namespace DeveloperOverwatch.Application.Queries.GetUsageWindows;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Each developer's estimated open 5-hour window, plus every plan holder without one; most-used first,
/// rows without a used fraction last, then by email.</summary>
public sealed record GetUsageWindowsQuery : IRequest<Result<IReadOnlyList<DeveloperWindowDto>>>;
