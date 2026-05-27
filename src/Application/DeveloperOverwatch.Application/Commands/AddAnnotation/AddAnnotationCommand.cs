namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record AddAnnotationCommand(
    string SessionId,
    string Content,
    string CreatedBy,
    string? TurnId = null,
    string? ToolExecutionId = null) : IRequest<Result<string>>;
