namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetSessionQuery(string SessionId) : IRequest<Result<Session>>;
