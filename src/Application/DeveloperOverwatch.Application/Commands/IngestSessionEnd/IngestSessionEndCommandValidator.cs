namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using FluentValidation;

public sealed class IngestSessionEndCommandValidator : AbstractValidator<IngestSessionEndCommand>
{
    public IngestSessionEndCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.EventId).NotEmpty().MaximumLength(64).When(x => x.EventId is not null);
        RuleFor(x => x.EndedAt).NotEmpty();
    }
}
