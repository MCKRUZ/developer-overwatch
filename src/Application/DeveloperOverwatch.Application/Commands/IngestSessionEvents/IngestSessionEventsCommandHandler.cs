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

        var results = new IngestItemResultsBuilder();
        var batch = await ValidateAsync(request, results, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        var totalEvents = await StoreAsync(session, batch, results, now, cancellationToken);
        if (totalEvents > 0)
            session.LastActivityAt = LatestActivity(session.LastActivityAt, batch.Stamps, now);

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<IngestSessionEventsResult>.Success(new IngestSessionEventsResult(totalEvents, results.Build()));
    }

    // Each item gets its own outcome: invalid items are rejected without sinking the batch, and
    // the rest are validated before deduping so a resend reports them the same way. Parents are
    // validated before their children, so a child can be checked against the parents the batch
    // keeps and the ones already stored.
    private async Task<ValidatedBatch> ValidateAsync(
        IngestSessionEventsCommand request, IngestItemResultsBuilder results, CancellationToken cancellationToken)
    {
        var turns = results.Valid(request.ConversationTurns, t => t.Id, IngestItemRules.Turn);

        // Conversation turn ids are derived from the transcript content (the API message id or
        // line uuid), not generated per send. A resumed Claude Code session copies the earlier
        // session's history, with the same ids, into its own transcript, so the existence check
        // for turns spans every session, not just this one. The same lookup covers the turns the
        // batch's tools reference, so a tool whose turn exists nowhere is rejected, not saved.
        var existingTurnIds = await _repository.GetExistingTurnIdsAsync(
            turns.Select(t => t.Id).Concat(ParentIds(request.ToolExecutions, t => t.TurnId)).ToList(),
            cancellationToken);
        var knownTurnIds = KnownIds(turns.Select(t => t.Id), existingTurnIds);
        var tools = results.Valid(
            request.ToolExecutions, t => t.Id,
            t => IngestItemRules.ToolExecution(t, results.RejectedIds, knownTurnIds));

        // Tool and file ids are primary keys, so the dedupe lookups span every session: an id stored under
        // another session is a duplicate, and inserting it again would fail the whole save. A file change's
        // parent, though, must be this session's: a new tool in the batch, or one stored under this session.
        var existingToolIds = await _repository.GetExistingToolExecutionIdsAsync(
            tools.Select(t => t.Id).ToList(), cancellationToken);
        var sessionToolIds = await _repository.GetToolExecutionIdsInSessionAsync(
            request.SessionId, ParentIds(request.FileChanges, f => f.ToolExecutionId).ToList(), cancellationToken);
        var knownToolIds = KnownIds(tools.Select(t => t.Id).Where(id => !existingToolIds.Contains(id)), sessionToolIds);
        var files = results.Valid(
            request.FileChanges, f => f.Id,
            f => IngestItemRules.FileChange(f, results.RejectedIds, knownToolIds));
        var existingFileIds = await _repository.GetExistingFileChangeIdsAsync(
            files.Select(f => f.Id).ToList(), cancellationToken);

        return new ValidatedBatch(
            turns, existingTurnIds, tools, existingToolIds, files, existingFileIds,
            results.Valid(request.ToolFailures, f => f.Id, IngestItemRules.ToolFailure),
            request.ActivityEvents.Where(IngestItemRules.IsValid).ToList());
    }

    // The Agent delivers at-least-once: a slow/timed-out response can look like a failure
    // client-side even though this exact batch already committed, so the same Ids get
    // resubmitted on retry. Treat resubmission as a no-op rather than double-counting
    // cost/tokens or crashing EF's change tracker on the identity conflict.
    private async Task<int> StoreAsync(
        Session session,
        ValidatedBatch batch,
        IngestItemResultsBuilder results,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var newTurns = results.New(batch.Turns, t => t.Id, batch.ExistingTurnIds);
        var newTools = results.New(batch.Tools, t => t.Id, batch.ExistingToolIds);
        var newFiles = results.New(batch.Files, f => f.Id, batch.ExistingFileIds);

        var totalEvents = await AddTurnsAsync(session, newTurns, cancellationToken);
        totalEvents += await AddToolExecutionsAsync(session, newTools, cancellationToken);
        totalEvents += await AddFileChangesAsync(session, newFiles, cancellationToken);
        totalEvents += ApplyActivity(session, batch.Activity, now);
        totalEvents += await ApplyToolFailuresAsync(session, batch.Failures, results, now, cancellationToken);
        return totalEvents;
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
        // (session.ConversationTurns is loaded with the session), so it re-numbers here instead
        // of trusting the client-sent value.
        var turnIndexBase = session.ConversationTurns.Count;

        var turns = newTurns.Select((t, i) =>
        {
            var turnIndex = turnIndexBase + i;
            var cost = _pricing.CalculateCost(
                t.Model, t.InputTokens, t.OutputTokens, t.CacheCreationInputTokens, t.CacheReadInputTokens);
            var contextTokens = ContextTokensOf(t);
            AccumulateUsage(session, t, cost, contextTokens);
            if (t.ContainsRedactedSecret)
                newViolations.Add(RedactedSecretViolation(session.Id, t.Id, turnIndex));
            return ToConversationTurn(session.Id, t, turnIndex, cost, contextTokens);
        }).ToList();
        await _repository.AddTurnsAsync(turns, cancellationToken);

        if (newViolations.Count > 0)
            await _violations.AddRangeAsync(newViolations, cancellationToken);

        return turns.Count;
    }

    private static void AccumulateUsage(Session session, ConversationTurnDto t, decimal cost, long? contextTokens)
    {
        session.TotalInputTokens += t.InputTokens ?? 0;
        session.TotalOutputTokens += t.OutputTokens ?? 0;
        session.TotalCacheCreationInputTokens += t.CacheCreationInputTokens ?? 0;
        session.TotalCacheReadInputTokens += t.CacheReadInputTokens ?? 0;
        session.EstimatedCostUsd += cost;

        // A subagent runs in its own context window, so only main-thread turns set the
        // session's peak. ContextTokens is still stored on every turn.
        if (t.Role != SubagentRole && contextTokens is long context
            && (session.PeakContextTokens is null || context > session.PeakContextTokens))
            session.PeakContextTokens = context;
    }

    // The Agent is the only place that ever sees the raw secret — by the time this
    // turn's content reaches here it's already redacted, so the server can't
    // re-detect it. Trust the Agent's flag and create the violation directly instead
    // of trying (and failing) to pattern-match already-scrubbed text.
    private static GovernanceViolation RedactedSecretViolation(string sessionId, string turnId, int turnIndex) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = sessionId,
        RuleId = "agent-redacted-secret",
        Severity = ViolationSeverity.Critical,
        Message = $"Agent redacted a likely secret in conversation turn {turnIndex}",
        ConversationTurnId = turnId,
        CreatedAt = DateTime.UtcNow
    };

    private ConversationTurn ToConversationTurn(
        string sessionId, ConversationTurnDto t, int turnIndex, decimal cost, long? contextTokens) => new()
    {
        Id = t.Id,
        SessionId = sessionId,
        TurnIndex = turnIndex,
        Role = t.Role!,
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
            ToolName = t.ToolName!,
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
            FilePath = f.FilePath!,
            BeforeHash = f.BeforeHash,
            AfterHash = f.AfterHash,
            DiffText = f.DiffText,
            ChangeType = f.ChangeType
        }), cancellationToken);
        return newFiles.Count;
    }

    private static IEnumerable<string> ParentIds<T>(IEnumerable<T> children, Func<T, string?> parentOf) =>
        children.Select(parentOf).OfType<string>().Distinct();

    private static IReadOnlySet<string> KnownIds(IEnumerable<string> batchIds, IReadOnlySet<string> storedIds) =>
        batchIds.Concat(storedIds).ToHashSet(StringComparer.Ordinal);

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
            ToolName = f.ToolName!,
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

    private sealed record ValidatedBatch(
        IReadOnlyList<ConversationTurnDto> Turns,
        IReadOnlySet<string> ExistingTurnIds,
        IReadOnlyList<ToolExecutionDto> Tools,
        IReadOnlySet<string> ExistingToolIds,
        IReadOnlyList<FileChangeDto> Files,
        IReadOnlySet<string> ExistingFileIds,
        IReadOnlyList<ToolFailureDto> Failures,
        IReadOnlyList<ActivityEventDto> Activity)
    {
        public IEnumerable<DateTime> Stamps =>
            Turns.Select(t => t.Timestamp)
                .Concat(Tools.Select(t => t.Timestamp))
                .Concat(Activity.Select(a => a.At))
                .Concat(Failures.Select(f => f.At));
    }
}
