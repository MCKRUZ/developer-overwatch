namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetAllAsync(string? developerEmail = null, string? repo = null, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default);
    Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default);
    Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default);
    Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default);
    Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default);
    Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default);
}
