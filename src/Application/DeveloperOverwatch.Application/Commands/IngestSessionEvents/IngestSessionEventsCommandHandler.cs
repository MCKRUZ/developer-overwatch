namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEventsCommandHandler
    : IRequestHandler<IngestSessionEventsCommand, Result<int>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionEventsCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        IngestSessionEventsCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<int>.Fail($"Session {request.SessionId} not found");

        var totalEvents = 0;

        if (request.ConversationTurns.Count > 0)
        {
            var turns = request.ConversationTurns.Select(t => new ConversationTurn
            {
                Id = t.Id,
                SessionId = request.SessionId,
                TurnIndex = t.TurnIndex,
                Role = t.Role,
                Content = t.Content,
                TokenCount = t.TokenCount,
                Timestamp = t.Timestamp,
                IsDecision = t.IsDecision
            });
            await _repository.AddTurnsAsync(turns, cancellationToken);
            totalEvents += request.ConversationTurns.Count;
        }

        if (request.ToolExecutions.Count > 0)
        {
            var tools = request.ToolExecutions.Select(t => new ToolExecution
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
            totalEvents += request.ToolExecutions.Count;
        }

        if (request.FileChanges.Count > 0)
        {
            var files = request.FileChanges.Select(f => new FileChange
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
            totalEvents += request.FileChanges.Count;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(totalEvents);
    }
}
