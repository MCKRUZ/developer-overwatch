namespace DeveloperOverwatch.Application.Queries.GetSessionSubagentBreakdown;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionSubagentBreakdownQueryHandler
    : IRequestHandler<GetSessionSubagentBreakdownQuery, Result<IReadOnlyList<SubagentUsage>>>
{
    /// <summary>The row for subagent turns the Agent could not name (their meta file was missing).</summary>
    public const string UnnamedSubagent = "(unnamed subagent)";

    private readonly ISessionInsightRepository _repository;

    public GetSessionSubagentBreakdownQueryHandler(ISessionInsightRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SubagentUsage>>> Handle(
        GetSessionSubagentBreakdownQuery request, CancellationToken cancellationToken)
    {
        if (!await _repository.SessionExistsAsync(request.SessionId, cancellationToken))
            return Result<IReadOnlyList<SubagentUsage>>.Fail($"Session {request.SessionId} not found");

        var turns = await _repository.GetTurnCostRowsAsync(request.SessionId, cancellationToken);
        var rows = turns
            .GroupBy(RowName)
            .Select(ToUsage)
            .OrderByDescending(r => r.EstimatedCostUsd)
            .ThenBy(r => r.SubagentName, StringComparer.Ordinal)
            .ToList();
        return Result<IReadOnlyList<SubagentUsage>>.Success(rows);
    }

    // The role decides main thread or subagent, as it does for the context reading; the name only labels the row.
    private static string? RowName(SessionTurnCostRow turn) =>
        turn.Role == SessionTurnCostRow.SubagentRole ? turn.SubagentName ?? UnnamedSubagent : null;

    private static SubagentUsage ToUsage(IGrouping<string?, SessionTurnCostRow> turns)
    {
        var input = turns.Sum(t => (long)(t.InputTokens ?? 0));
        var output = turns.Sum(t => (long)(t.OutputTokens ?? 0));
        var write = turns.Sum(t => (long)(t.CacheCreationInputTokens ?? 0));
        var read = turns.Sum(t => (long)(t.CacheReadInputTokens ?? 0));
        return new SubagentUsage(
            turns.Key,
            turns.Count(),
            input,
            output,
            write,
            read,
            input + output + write + read,
            turns.Sum(t => t.EstimatedCostUsd ?? 0m));
    }
}
