namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEventsCommandHandler
    : IRequestHandler<IngestSessionEventsCommand, Result<int>>
{
    // The role the Agent gives every turn produced inside a subagent.
    private const string SubagentRole = "subagent";

    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenPricingCalculator _pricing;
    private readonly IGovernanceViolationRepository _violations;

    public IngestSessionEventsCommandHandler(
        ISessionRepository repository,
        IUnitOfWork unitOfWork,
        ITokenPricingCalculator pricing,
        IGovernanceViolationRepository violations)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _pricing = pricing;
        _violations = violations;
    }

    public async Task<Result<int>> Handle(
        IngestSessionEventsCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<int>.Fail($"Session {request.SessionId} not found");

        // Picks up a mid-session `/rename` on the next batch — SessionStart only sees the title
        // at session creation, but hooks re-read the local custom-title.json on every event batch.
        if (!string.IsNullOrWhiteSpace(request.SessionTitle) && session.Title != request.SessionTitle)
            session.Title = request.SessionTitle;

        // The Agent delivers at-least-once: a slow/timed-out response can look like a failure
        // client-side even though this exact batch already committed, so the same Ids get
        // resubmitted on retry. Treat resubmission as a no-op rather than double-counting
        // cost/tokens or crashing EF's change tracker on the identity conflict.
        //
        // Conversation turn ids are derived from the transcript content (the API message id or
        // line uuid), not generated per send. A resumed Claude Code session copies the earlier
        // session's history, with the same ids, into its own transcript, so the existence check
        // for turns spans every session, not just this one.
        var incomingTurns = request.ConversationTurns.DistinctBy(t => t.Id).ToList();
        var existingTurnIds = await _repository.GetExistingTurnIdsAsync(
            incomingTurns.Select(t => t.Id).ToList(), cancellationToken);
        var existingToolIds = session.ToolExecutions.Select(t => t.Id).ToHashSet();
        var existingFileIds = session.FileChanges.Select(f => f.Id).ToHashSet();

        var newConversationTurns = incomingTurns.Where(t => !existingTurnIds.Contains(t.Id)).ToList();
        var newToolExecutions = request.ToolExecutions.Where(t => !existingToolIds.Contains(t.Id)).ToList();
        var newFileChanges = request.FileChanges.Where(f => !existingFileIds.Contains(f.Id)).ToList();

        var totalEvents = 0;

        if (newConversationTurns.Count > 0)
        {
            var newViolations = new List<GovernanceViolation>();

            // The Agent assigns TurnIndex from its own file-reading cursor, which counts raw
            // transcript LINES (tool calls, thinking blocks, system entries — everything, not
            // just turns) — so its value can run into the hundreds/thousands even for a session
            // with a few dozen real turns. Only the server knows the true sequential turn count
            // (session.ConversationTurns is already loaded above), so it re-numbers here instead
            // of trusting the client-sent value.
            var turnIndexBase = session.ConversationTurns.Count;

            var turns = newConversationTurns.Select((t, i) =>
            {
                var turnIndex = turnIndexBase + i;
                var cost = _pricing.CalculateCost(
                    t.Model, t.InputTokens, t.OutputTokens, t.CacheCreationInputTokens, t.CacheReadInputTokens);

                session.TotalInputTokens += t.InputTokens ?? 0;
                session.TotalOutputTokens += t.OutputTokens ?? 0;
                session.TotalCacheCreationInputTokens += t.CacheCreationInputTokens ?? 0;
                session.TotalCacheReadInputTokens += t.CacheReadInputTokens ?? 0;
                session.EstimatedCostUsd += cost;

                // A subagent runs in its own context window, so only main-thread turns set the
                // session's peak. ContextTokens is still stored on every turn.
                var contextTokens = ContextTokensOf(t);
                if (t.Role != SubagentRole && contextTokens is long context
                    && (session.PeakContextTokens is null || context > session.PeakContextTokens))
                    session.PeakContextTokens = context;

                // The Agent is the only place that ever sees the raw secret — by the time this
                // turn's content reaches here it's already redacted, so the server can't
                // re-detect it. Trust the Agent's flag and create the violation directly instead
                // of trying (and failing) to pattern-match already-scrubbed text.
                if (t.ContainsRedactedSecret)
                {
                    newViolations.Add(new GovernanceViolation
                    {
                        Id = Guid.NewGuid().ToString(),
                        SessionId = request.SessionId,
                        RuleId = "agent-redacted-secret",
                        Severity = ViolationSeverity.Critical,
                        Message = $"Agent redacted a likely secret in conversation turn {turnIndex}",
                        ConversationTurnId = t.Id,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                return new ConversationTurn
                {
                    Id = t.Id,
                    SessionId = request.SessionId,
                    TurnIndex = turnIndex,
                    Role = t.Role,
                    Content = t.Content,
                    TokenCount = t.TokenCount,
                    InputTokens = t.InputTokens,
                    OutputTokens = t.OutputTokens,
                    CacheCreationInputTokens = t.CacheCreationInputTokens,
                    CacheReadInputTokens = t.CacheReadInputTokens,
                    Model = t.Model,
                    EstimatedCostUsd = cost,
                    PricesAsOf = _pricing.PricesAsOf,
                    ContainsRedactedSecret = t.ContainsRedactedSecret,
                    Timestamp = t.Timestamp,
                    IsDecision = t.IsDecision,
                    GitBranch = t.GitBranch,
                    SubagentName = t.SubagentName,
                    ContextTokens = contextTokens
                };
            }).ToList();
            await _repository.AddTurnsAsync(turns, cancellationToken);
            totalEvents += newConversationTurns.Count;

            if (newViolations.Count > 0)
                await _violations.AddRangeAsync(newViolations, cancellationToken);
        }

        if (newToolExecutions.Count > 0)
        {
            var tools = newToolExecutions.Select(t => new ToolExecution
            {
                Id = t.Id,
                SessionId = request.SessionId,
                TurnId = t.TurnId,
                ToolName = t.ToolName,
                InputParams = t.InputParams,
                OutputSummary = t.OutputSummary,
                DurationMs = t.DurationMs,
                Timestamp = t.Timestamp
            });
            await _repository.AddToolExecutionsAsync(tools, cancellationToken);
            totalEvents += newToolExecutions.Count;
        }

        if (newFileChanges.Count > 0)
        {
            var files = newFileChanges.Select(f => new FileChange
            {
                Id = f.Id,
                SessionId = request.SessionId,
                ToolExecutionId = f.ToolExecutionId,
                FilePath = f.FilePath,
                BeforeHash = f.BeforeHash,
                AfterHash = f.AfterHash,
                DiffText = f.DiffText,
                ChangeType = f.ChangeType
            });
            await _repository.AddFileChangesAsync(files, cancellationToken);
            totalEvents += newFileChanges.Count;
        }

        totalEvents += ApplyActivity(session, request.ActivityEvents);
        totalEvents += await ApplyToolFailuresAsync(session, request.ToolFailures, cancellationToken);

        if (totalEvents > 0)
            session.LastActivityAt = DateTime.UtcNow;

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(totalEvents);
    }

    private static long? ContextTokensOf(ConversationTurnDto t) =>
        t.InputTokens is null && t.CacheReadInputTokens is null && t.CacheCreationInputTokens is null
            ? null
            : (long)(t.InputTokens ?? 0) + (t.CacheReadInputTokens ?? 0) + (t.CacheCreationInputTokens ?? 0);

    // Batches can arrive out of order (retries), so only a state newer than the stored one wins.
    private static int ApplyActivity(Session session, IReadOnlyList<ActivityEventDto> events)
    {
        if (events.Count == 0)
            return 0;

        var latest = events.MaxBy(e => e.At)!;
        if (session.ActivityStateAt is not null && latest.At <= session.ActivityStateAt)
            return 0;

        session.ActivityState = latest.State;
        session.ActivityStateAt = latest.At;
        return 1;
    }

    // Delivery is at-least-once, so a failure can arrive again. Each is stored once by id, and the
    // cached count and latest name on the session move only for ids not seen before.
    private async Task<int> ApplyToolFailuresAsync(
        Session session, IReadOnlyList<ToolFailureDto> failures, CancellationToken cancellationToken)
    {
        if (failures.Count == 0)
            return 0;

        var incoming = failures.DistinctBy(f => f.Id).ToList();
        var existingIds = await _repository.GetExistingToolFailureIdsAsync(
            incoming.Select(f => f.Id).ToList(), cancellationToken);
        var newFailures = incoming.Where(f => !existingIds.Contains(f.Id)).ToList();
        if (newFailures.Count == 0)
            return 0;

        await _repository.AddToolFailuresAsync(newFailures.Select(f => new SessionToolFailure
        {
            Id = f.Id,
            SessionId = session.Id,
            ToolName = f.ToolName,
            At = f.At
        }).ToList(), cancellationToken);

        session.ToolErrorCount += newFailures.Count;
        var latest = newFailures.MaxBy(f => f.At)!;
        if (session.LastToolErrorAt is null || latest.At >= session.LastToolErrorAt)
        {
            session.LastToolErrorName = latest.ToolName;
            session.LastToolErrorAt = latest.At;
        }

        return newFailures.Count;
    }
}
