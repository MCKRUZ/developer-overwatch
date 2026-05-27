namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class AddAnnotationCommandHandler
    : IRequestHandler<AddAnnotationCommand, Result<string>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IArchitectAnnotationRepository _annotationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AddAnnotationCommandHandler(
        ISessionRepository sessionRepository,
        IArchitectAnnotationRepository annotationRepository,
        IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _annotationRepository = annotationRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(AddAnnotationCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<string>.Fail($"Session '{request.SessionId}' not found");

        var annotation = new ArchitectAnnotation
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = request.SessionId,
            TurnId = request.TurnId,
            ToolExecutionId = request.ToolExecutionId,
            Content = request.Content,
            CreatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow
        };

        await _annotationRepository.AddAsync(annotation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(annotation.Id);
    }
}
