namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEventsCommandHandler
    : IRequestHandler<IngestSessionEventsCommand, Result<IngestSessionEventsResult>>
{
    // The role the Agent gives every turn produced inside a subagent.
    private const string SubagentRole = "subagent";

    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenPricingCalculator _pricing;
    private readonly IGovernanceViolationRepository _violations;
    private readonly TimeProvider _time;

    public IngestSessionEventsCommandHandler(
        ISessionRepository repository,
        IUnitOfWork unitOfWork,
        ITokenPricingCalculator pricing,
        IGovernanceViolationRepository violations,
        TimeProvider time)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _pricing = pricing;
        _violations = violations;
        _time = time;
    }

    public async Task<Result<IngestSessionEventsResult>> Handle(
        IngestSessionEventsCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<IngestSessionEventsResult>.Fail($"Session {request.SessionId} not found");

        // Picks up a mid-session `/rename` on the next batch — SessionStart only sees the title
        // at session creation, but hooks re-read the local custom-title.json on every event batch.
        if (!string.IsNullOrWhiteSpace(request.SessionTitle) && session.Title != request.SessionTitle)
            session.Title = request.SessionTitle;

        // Each item gets its own outcome: invalid items are rejected without sinking the batch, and
        // the rest are validated before deduping so a resend reports them the same way.
        var results = new IngestItemResultsBuilder();
        var validTurns = results.Valid(request.ConversationTurns, t => t.Id, IngestItemRules.Turn);
        var validTools = results.Valid(
            request.ToolExecutions, t => t.Id, t => IngestItemRules.ToolExecution(t, results.RejectedIds));
        var validFiles = results.Valid(
            request.FileChanges, f => f.Id, f => IngestItemRules.FileChange(f, results.RejectedIds));
        var validFailures = results.Valid(request.ToolFailures, f => f.Id, IngestItemRules.ToolFailure);
        var activity = request.ActivityEvents.Where(IngestItemRules.IsValid).ToList();

        // The Agent delivers at-least-once: a slow/timed-out response can look like a failure
        // client-side even though this exact batch already committed, so the same Ids get
        // resubmitted on retry. Treat resubmission as a no-op rather than double-counting
        // cost/tokens or crashing EF's change tracker on the identity conflict.
        //
        // Conversation turn ids are derived from the transcript content (the API message id or
        // line uuid), not generated per send. A resumed Claude Code session copies the earlier
        // session's history, with the same ids, into its own transcript, so the existence check
        // for turns spans every session, not just this one.
        var existingTurnIds = await _repository.GetExistingTurnIdsAsync(
            validTurns.Select(t => t.Id).ToList(), cancellationToken);
        var newConversationTurns = results.New(validTurns, t => t.Id, existingTurnIds);
        var newToolExecutions = results.New(
            validTools, t => t.Id, session.ToolExecutions.Select(t => t.Id).ToHashSet());
        var newFileChanges = results.New(
            validFiles, f => f.Id, session.FileChanges.Select(f => f.Id).ToHashSet());

        var now = _time.GetUtcNow().UtcDateTime;
        var totalEvents = await AddTurnsAsync(session, newConversationTurns, cancellationToken);
        totalEvents += await AddToolExecutionsAsync(session, newToolExecutions, cancellationToken);
        totalEvents += await AddFileChangesAsync(session, newFileChanges, cancellationToken);
        totalEvents += ApplyActivity(session, activity, now);
        totalEvents += await ApplyToolFailuresAsync(session, validFailures, results, now, cancellationToken);

        if (totalEvents > 0)
        {
            var stamps = validTurns.Select(t => t.Timestamp)
                .Concat(validTools.Select(t => t.Timestamp))
                .Concat(activity.Select(a => a.At))
                .Concat(validFailures.Select(f => f.At));
            session.LastActivityAt = LatestActivity(session.LastActivityAt, stamps, now);
        }

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<IngestSessionEventsResult>.Success(new IngestSessionEventsResult(totalEvents, results.Build()));
    }

    private async Task<int> AddTurnsAsync(
        Session session, IReadOnlyList<ConversationTurnDto> newTurns, CancellationToken cancellationToken)
    {
        if (newTurns.Count == 0)
            return 0;

        var newViolations = new List<GovernanceViolation>();

        // The Agent assigns TurnIndex from its own file-reading cursor, which counts raw
        // transcript LINES (tool calls, thinking blocks, system entries — everything, not
        // just turns) — so its value can run into the hundreds/thousands even for a session
        // with a few dozen real turns. Only the server knows the true sequential turn count
        // (session.ConversationTurns is already loaded above), so it re-numbers here instead
        // of trusting the client-sent value.
        var turnIndexBase = session.ConversationTurns.Count;

        var turns = newTurns.Select((t, i) =>
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
                    SessionId = session.Id,
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
                SessionId = session.Id,
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

        if (newViolations.Count > 0)
            await _violations.AddRangeAsync(newViolations, cancellationToken);

        return turns.Count;
    }

    private async Task<int> AddToolExecutionsAsync(
        Session session, IReadOnlyList<ToolExecutionDto> newTools, CancellationToken cancellationToken)
    {
        if (newTools.Count == 0)
            return 0;

        await _repository.AddToolExecutionsAsync(newTools.Select(t => new ToolExecution
        {
            Id = t.Id,
            SessionId = session.Id,
            TurnId = t.TurnId,
            ToolName = t.ToolName,
            InputParams = t.InputParams,
            OutputSummary = t.OutputSummary,
            DurationMs = t.DurationMs,
            Timestamp = t.Timestamp
        }), cancellationToken);
        return newTools.Count;
    }

    private async Task<int> AddFileChangesAsync(
        Session session, IReadOnlyList<FileChangeDto> newFiles, CancellationToken cancellationToken)
    {
        if (newFiles.Count == 0)
            return 0;

        await _repository.AddFileChangesAsync(newFiles.Select(f => new FileChange
        {
            Id = f.Id,
            SessionId = session.Id,
            ToolExecutionId = f.ToolExecutionId,
            FilePath = f.FilePath,
            BeforeHash = f.BeforeHash,
            AfterHash = f.AfterHash,
            DiffText = f.DiffText,
            ChangeType = f.ChangeType
        }), cancellationToken);
        return newFiles.Count;
    }

    // The latest moment the batch describes, never earlier than what is stored, so backfilled history
    // does not make an old session look active now. Capped at now, so a fast client clock cannot push it
    // into the future. A batch with no timestamps counts as activity on arrival.
    private static DateTime LatestActivity(DateTime? stored, IEnumerable<DateTime> stamps, DateTime now)
    {
        var utc = stamps.Select(ToUtc).ToList();
        if (utc.Count == 0)
            return now;

        var latest = AtMost(utc.Max(), now);
        return stored is { } s && s > latest ? s : latest;
    }

    private static DateTime ToUtc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;

    // A fast client clock must not push stored times into the future, where a state that only a
    // newer timestamp replaces would stay pinned. Clamped rather than rejected: the Agent can never
    // succeed on a retry of a rejected item.
    private static DateTime AtMost(DateTime at, DateTime now) => ToUtc(at) > now ? now : at;

    private static long? ContextTokensOf(ConversationTurnDto t) =>
        t.InputTokens is null && t.CacheReadInputTokens is null && t.CacheCreationInputTokens is null
            ? null
            : (long)(t.InputTokens ?? 0) + (t.CacheReadInputTokens ?? 0) + (t.CacheCreationInputTokens ?? 0);

    // Batches can arrive out of order (retries), so only a state newer than the stored one wins. The
    // latest event is chosen by its raw time and only then clamped: clamping first would make every
    // future-stamped event tie at now, and the first in the batch would win.
    private static int ApplyActivity(Session session, IReadOnlyList<ActivityEventDto> events, DateTime now)
    {
        if (events.Count == 0)
            return 0;

        var latest = events.MaxBy(e => ToUtc(e.At))!;
        var at = AtMost(latest.At, now);
        if (session.ActivityStateAt is not null && at <= session.ActivityStateAt)
            return 0;

        session.ActivityState = latest.State;
        session.ActivityStateAt = at;
        return 1;
    }

    // Delivery is at-least-once, so a failure can arrive again. Each is stored once by id, and the
    // cached count and latest name on the session move only for ids not seen before.
    private async Task<int> ApplyToolFailuresAsync(
        Session session,
        IReadOnlyList<ToolFailureDto> failures,
        IngestItemResultsBuilder results,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (failures.Count == 0)
            return 0;

        var existingIds = await _repository.GetExistingToolFailureIdsAsync(
            failures.Select(f => f.Id).ToList(), cancellationToken);
        var newFailures = results.New(failures, f => f.Id, existingIds);
        if (newFailures.Count == 0)
            return 0;

        await _repository.AddToolFailuresAsync(newFailures.Select(f => new SessionToolFailure
        {
            Id = f.Id,
            SessionId = session.Id,
            ToolName = f.ToolName,
            At = AtMost(f.At, now)
        }).ToList(), cancellationToken);

        session.ToolErrorCount += newFailures.Count;
        var latest = newFailures.MaxBy(f => ToUtc(f.At))!;
        var latestAt = AtMost(latest.At, now);
        if (session.LastToolErrorAt is null || latestAt >= session.LastToolErrorAt)
        {
            session.LastToolErrorName = latest.ToolName;
            session.LastToolErrorAt = latestAt;
        }

        return newFailures.Count;
    }
}
