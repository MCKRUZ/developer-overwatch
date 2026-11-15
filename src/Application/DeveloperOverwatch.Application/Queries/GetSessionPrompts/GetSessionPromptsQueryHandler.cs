namespace DeveloperOverwatch.Application.Queries.GetSessionPrompts;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionPromptsQueryHandler
    : IRequestHandler<GetSessionPromptsQuery, Result<IReadOnlyList<PromptCost>>>
{
    private readonly ISessionInsightRepository _repository;

    public GetSessionPromptsQueryHandler(ISessionInsightRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<PromptCost>>> Handle(
        GetSessionPromptsQuery request, CancellationToken cancellationToken)
    {
        if (!await _repository.SessionExistsAsync(request.SessionId, cancellationToken))
            return Result<IReadOnlyList<PromptCost>>.Fail($"Session {request.SessionId} not found");

        var turns = await _repository.GetTurnCostRowsAsync(request.SessionId, cancellationToken);
        return Result<IReadOnlyList<PromptCost>>.Success(Group(turns));
    }

    // Each group starts at a prompt and runs to the next one; turns before the first prompt form group 0.
    private static IReadOnlyList<PromptCost> Group(IReadOnlyList<SessionTurnCostRow> turns)
    {
        var groups = new List<List<SessionTurnCostRow>>();
        foreach (var turn in turns)
        {
            if (StartsPrompt(turn) || groups.Count == 0)
                groups.Add([turn]);
            else
                groups[^1].Add(turn);
        }

        var firstIndex = StartsPrompt(groups.FirstOrDefault()?[0]) ? 1 : 0;
        return groups.Select((g, i) => ToPromptCost(firstIndex + i, g)).ToList();
    }

    // Agents older than the turn kind send none, and then a user turn is the developer's prompt.
    private static bool StartsPrompt(SessionTurnCostRow? turn) =>
        turn is { Role: SessionTurnCostRow.UserRole }
        && turn.TurnKind is null or TurnKinds.Prompt;

    private static PromptCost ToPromptCost(int index, IReadOnlyList<SessionTurnCostRow> turns)
    {
        var input = turns.Sum(t => (long)(t.InputTokens ?? 0));
        var output = turns.Sum(t => (long)(t.OutputTokens ?? 0));
        var write = turns.Sum(t => (long)(t.CacheCreationInputTokens ?? 0));
        var read = turns.Sum(t => (long)(t.CacheReadInputTokens ?? 0));
        return new PromptCost(
            index,
            turns[0].Timestamp,
            index == 0 ? null : PromptLabel.From(turns[0].PromptText),
            turns.Count,
            input,
            output,
            write,
            read,
            input + output + write + read,
            turns.Sum(t => t.EstimatedCostUsd ?? 0m),
            turns.Where(t => t.SubagentName is not null).Sum(t => t.EstimatedCostUsd ?? 0m));
    }
}
