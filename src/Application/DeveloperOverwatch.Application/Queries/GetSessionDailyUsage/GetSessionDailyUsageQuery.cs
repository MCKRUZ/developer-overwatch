namespace DeveloperOverwatch.Application.Queries.GetSessionDailyUsage;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetSessionDailyUsageQuery(string SessionId) : IRequest<Result<IReadOnlyList<DailyUsage>>>;

public sealed record DailyUsage(
    DateOnly Date,
    int TurnCount,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd);
