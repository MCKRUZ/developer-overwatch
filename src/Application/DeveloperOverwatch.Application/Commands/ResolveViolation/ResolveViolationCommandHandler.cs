namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ResolveViolationCommandHandler
    : IRequestHandler<ResolveViolationCommand, Result<string>>
{
    private readonly IGovernanceViolationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ResolveViolationCommandHandler(IGovernanceViolationRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(ResolveViolationCommand request, CancellationToken cancellationToken)
    {
        var violation = await _repository.GetByIdAsync(request.ViolationId, cancellationToken);
        if (violation is null)
            return Result<string>.Fail($"Violation {request.ViolationId} not found");

        if (violation.Resolved)
            return Result<string>.Fail($"Violation {request.ViolationId} is already resolved");

        violation.Resolved = true;
        violation.ResolvedBy = request.ResolvedBy;
        violation.ResolutionReason = request.ResolutionReason;
        violation.ResolvedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(violation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(violation.Id);
    }
}
