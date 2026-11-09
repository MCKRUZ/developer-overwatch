namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class SessionRepository : ISessionRepository
{
    private readonly OverwatchDbContext _db;

    public SessionRepository(OverwatchDbContext db) => _db = db;

    public async Task<Session?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.Sessions
            // Five Includes in a single query join-multiplies rows across every collection
            // (turns x tools x files x violations x annotations) — for a session with hundreds
            // of turns and tools this Cartesian-explodes into an enormous result set that gets
            // slower every time the session grows. AsSplitQuery issues one query per collection
            // instead, avoiding the multiplication while still eager-loading everything.
            .AsSplitQuery()
            .Include(s => s.ConversationTurns.OrderBy(t => t.TurnIndex))
            .Include(s => s.ToolExecutions.OrderBy(t => t.Timestamp))
            .Include(s => s.FileChanges)
            .Include(s => s.GovernanceViolations)
            .Include(s => s.ArchitectAnnotations)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<SessionDetailProjection?> GetDetailAsync(string id, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Include(s => s.GovernanceViolations)
            .Include(s => s.ArchitectAnnotations)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null)
            return null;

        var turnCount = await _db.ConversationTurns.CountAsync(t => t.SessionId == id, ct);
        var toolCount = await _db.ToolExecutions.CountAsync(t => t.SessionId == id, ct);
        var fileCount = await _db.FileChanges.CountAsync(f => f.SessionId == id, ct);

        return new SessionDetailProjection(session, turnCount, toolCount, fileCount);
    }

    public async Task<IReadOnlyList<Session>> GetAllAsync(
        string? developerEmail = null,
        string? repo = null,
        DateTime? from = null,
        DateTime? to = null,
        SessionStatus? status = null,
        CancellationToken ct = default)
    {
        var query = _db.Sessions.AsQueryable();
        if (developerEmail is not null)
            query = query.Where(s => s.DeveloperEmail == developerEmail);
        if (repo is not null)
            query = query.Where(s => s.RepoPath == repo);

        // Active sessions bypass the date range: a resumed session that started days ago is
        // still happening right now and shouldn't disappear just because its StartedAt predates
        // the filter window (e.g. a "Today" default filter). Only skipped when the caller
        // explicitly filters to a non-Active status — that's a deliberate ask for something else.
        var bypassDateForActive = status is null || status == SessionStatus.Active;
        if (from is not null || to is not null)
        {
            query = query.Where(s =>
                (from == null || s.StartedAt >= from) && (to == null || s.StartedAt < to)
                || (bypassDateForActive && s.Status == SessionStatus.Active));
        }
        if (status is not null)
            query = query.Where(s => s.Status == status);
        return await query.OrderByDescending(s => s.StartedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetViolationCountsAsync(
        IEnumerable<string> sessionIds, CancellationToken ct = default)
    {
        var ids = sessionIds.ToArray();
        return await _db.GovernanceViolations
            .Where(v => ids.Contains(v.SessionId))
            .GroupBy(v => v.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, ct);
    }

    public async Task<TimelinePage> GetTimelinePageAsync(
        string sessionId, DateTime? before, int limit, CancellationToken ct = default)
    {
        var turnsQuery = _db.ConversationTurns.Where(t => t.SessionId == sessionId);
        var toolsQuery = _db.ToolExecutions.Where(t => t.SessionId == sessionId);
        if (before is not null)
        {
            turnsQuery = turnsQuery.Where(t => t.Timestamp < before);
            toolsQuery = toolsQuery.Where(t => t.Timestamp < before);
        }

        // Overfetch `limit` candidates from each source, merge-sort by timestamp, then trim to
        // the true top `limit` overall — cheap (bounded to 2*limit rows) and correct regardless
        // of how turns/tools interleave chronologically.
        var turnCandidates = await turnsQuery
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToListAsync(ct);
        var toolCandidates = await toolsQuery
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        var mergedTurns = new List<ConversationTurn>();
        var mergedTools = new List<ToolExecution>();
        var turnIdx = 0;
        var toolIdx = 0;
        while (mergedTurns.Count + mergedTools.Count < limit &&
               (turnIdx < turnCandidates.Count || toolIdx < toolCandidates.Count))
        {
            var takeTurn =
                toolIdx >= toolCandidates.Count ||
                (turnIdx < turnCandidates.Count &&
                 turnCandidates[turnIdx].Timestamp >= toolCandidates[toolIdx].Timestamp);

            if (takeTurn)
                mergedTurns.Add(turnCandidates[turnIdx++]);
            else
                mergedTools.Add(toolCandidates[toolIdx++]);
        }

        var toolIds = mergedTools.Select(t => t.Id).ToArray();
        var fileChanges = await GetFileChangesForToolsAsync(toolIds, ct);

        var oldestIncluded = mergedTurns.Select(t => (DateTime?)t.Timestamp)
            .Concat(mergedTools.Select(t => (DateTime?)t.Timestamp))
            .Where(ts => ts is not null)
            .DefaultIfEmpty(null)
            .Min();

        var hasMore = turnIdx < turnCandidates.Count || toolIdx < toolCandidates.Count ||
                      turnCandidates.Count == limit || toolCandidates.Count == limit;

        return new TimelinePage(mergedTurns, mergedTools, fileChanges, oldestIncluded, hasMore);
    }

    public async Task<TimelinePage?> GetTimelinePageAroundAsync(
        string sessionId, string? turnId, string? toolExecutionId, int limit, CancellationToken ct = default)
    {
        DateTime? anchor = turnId is not null
            ? await _db.ConversationTurns
                .Where(t => t.Id == turnId && t.SessionId == sessionId)
                .Select(t => (DateTime?)t.Timestamp)
                .FirstOrDefaultAsync(ct)
            : toolExecutionId is not null
                ? await _db.ToolExecutions
                    .Where(t => t.Id == toolExecutionId && t.SessionId == sessionId)
                    .Select(t => (DateTime?)t.Timestamp)
                    .FirstOrDefaultAsync(ct)
                : null;

        if (anchor is null)
            return null;

        var half = Math.Max(1, limit / 2);
        var turnsQuery = _db.ConversationTurns.Where(t => t.SessionId == sessionId);
        var toolsQuery = _db.ToolExecutions.Where(t => t.SessionId == sessionId);

        // Half the page is the anchor forward (itself + what happened next), half is what led
        // up to it — the reader gets context on both sides of the violation instead of just the
        // single flagged turn/tool.
        var newerTurns = await turnsQuery.Where(t => t.Timestamp >= anchor)
            .OrderBy(t => t.Timestamp).Take(half).ToListAsync(ct);
        var newerTools = await toolsQuery.Where(t => t.Timestamp >= anchor)
            .OrderBy(t => t.Timestamp).Take(half).ToListAsync(ct);
        var olderTurns = await turnsQuery.Where(t => t.Timestamp < anchor)
            .OrderByDescending(t => t.Timestamp).Take(half).ToListAsync(ct);
        var olderTools = await toolsQuery.Where(t => t.Timestamp < anchor)
            .OrderByDescending(t => t.Timestamp).Take(half).ToListAsync(ct);

        var mergedTurns = newerTurns.Concat(olderTurns).OrderByDescending(t => t.Timestamp).ToList();
        var mergedTools = newerTools.Concat(olderTools).OrderByDescending(t => t.Timestamp).ToList();

        var toolIds = mergedTools.Select(t => t.Id).ToArray();
        var fileChanges = await GetFileChangesForToolsAsync(toolIds, ct);

        var oldestIncluded = mergedTurns.Select(t => (DateTime?)t.Timestamp)
            .Concat(mergedTools.Select(t => (DateTime?)t.Timestamp))
            .Where(ts => ts is not null)
            .DefaultIfEmpty(null)
            .Min();

        var hasMore = olderTurns.Count == half || olderTools.Count == half;

        return new TimelinePage(mergedTurns, mergedTools, fileChanges, oldestIncluded, hasMore);
    }

    private async Task<IReadOnlyList<FileChange>> GetFileChangesForToolsAsync(
        IReadOnlyCollection<string> toolIds, CancellationToken ct) =>
        toolIds.Count == 0
            ? []
            : await _db.FileChanges
                .Where(f => f.ToolExecutionId != null && toolIds.Contains(f.ToolExecutionId!))
                .ToListAsync(ct);

    public async Task<IReadOnlyList<TurnUsageRow>> GetTurnUsageRowsAsync(
        string sessionId, CancellationToken ct = default) =>
        await _db.ConversationTurns
            .Where(t => t.SessionId == sessionId)
            .Select(t => new TurnUsageRow(
                t.Timestamp, t.Model, t.InputTokens, t.OutputTokens,
                t.CacheCreationInputTokens, t.CacheReadInputTokens, t.EstimatedCostUsd))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TurnUsageRow>> GetTurnUsageRowsForDateRangeAsync(
        DateTime? from, DateTime? to, string? developerEmail = null, CancellationToken ct = default)
    {
        var query = _db.ConversationTurns.AsQueryable();
        if (from is not null)
            query = query.Where(t => t.Timestamp >= from);
        if (to is not null)
            query = query.Where(t => t.Timestamp < to);
        if (developerEmail is not null)
        {
            query = query.Where(t => _db.Sessions
                .Any(s => s.Id == t.SessionId && s.DeveloperEmail == developerEmail));
        }

        return await query
            .Select(t => new TurnUsageRow(
                t.Timestamp, t.Model, t.InputTokens, t.OutputTokens,
                t.CacheCreationInputTokens, t.CacheReadInputTokens, t.EstimatedCostUsd))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SessionTurnUsageRow>> GetTurnUsageRowsForSessionsAsync(
        IEnumerable<string> sessionIds, CancellationToken ct = default)
    {
        var ids = sessionIds.ToArray();
        if (ids.Length == 0)
            return [];

        return await _db.ConversationTurns
            .Where(t => ids.Contains(t.SessionId))
            .Select(t => new SessionTurnUsageRow(
                t.SessionId, t.Timestamp, t.InputTokens, t.OutputTokens,
                t.CacheCreationInputTokens, t.CacheReadInputTokens, t.EstimatedCostUsd))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Session session, CancellationToken ct = default) =>
        await _db.Sessions.AddAsync(session, ct);

    // SQL Server caps a query at 2,100 parameters; a first capture of a long transcript can send
    // thousands of turns, so the lookup runs in chunks.
    private const int TurnIdLookupChunkSize = 1000;

    public async Task<IReadOnlySet<string>> GetExistingTurnIdsAsync(
        IReadOnlyCollection<string> turnIds, CancellationToken ct = default)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in turnIds.Distinct().Chunk(TurnIdLookupChunkSize))
        {
            var found = await _db.ConversationTurns
                .Where(t => chunk.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            existing.UnionWith(found);
        }
        return existing;
    }

    public async Task<IReadOnlySet<string>> GetExistingToolFailureIdsAsync(
        IReadOnlyCollection<string> failureIds, CancellationToken ct = default)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in failureIds.Distinct().Chunk(TurnIdLookupChunkSize))
        {
            var found = await _db.SessionToolFailures
                .Where(f => chunk.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(ct);
            existing.UnionWith(found);
        }
        return existing;
    }

    public async Task AddToolFailuresAsync(IEnumerable<SessionToolFailure> failures, CancellationToken ct = default) =>
        await _db.SessionToolFailures.AddRangeAsync(failures, ct);

    public Task<bool> IsAgentEventProcessedAsync(string sessionId, string eventId, CancellationToken ct = default) =>
        _db.ProcessedAgentEvents.AnyAsync(e => e.SessionId == sessionId && e.EventId == eventId, ct);

    public async Task AddProcessedAgentEventAsync(ProcessedAgentEvent processedEvent, CancellationToken ct = default) =>
        await _db.ProcessedAgentEvents.AddAsync(processedEvent, ct);

    public Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        _db.Sessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddAsync(turn, ct);

    public async Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddAsync(toolExecution, ct);

    public async Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default) =>
        await _db.FileChanges.AddAsync(fileChange, ct);

    public async Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddRangeAsync(turns, ct);

    public async Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddRangeAsync(toolExecutions, ct);

    public async Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default) =>
        await _db.FileChanges.AddRangeAsync(fileChanges, ct);
}
