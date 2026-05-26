namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetSessionsQuery(
    string? DeveloperEmail = null,
    string? Repo = null) : IRequest<Result<IReadOnlyList<Session>>>;
