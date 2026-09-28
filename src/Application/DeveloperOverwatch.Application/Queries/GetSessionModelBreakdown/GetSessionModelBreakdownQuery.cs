namespace DeveloperOverwatch.Application.Queries.GetSessionModelBreakdown;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetSessionModelBreakdownQuery(string SessionId) : IRequest<Result<IReadOnlyList<ModelUsage>>>;

public sealed record ModelUsage(
    string Model,
    int TurnCount,
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    decimal EstimatedCostUsd);
