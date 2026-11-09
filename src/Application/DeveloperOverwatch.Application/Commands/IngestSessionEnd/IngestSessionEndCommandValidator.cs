namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using FluentValidation;

public sealed class IngestSessionEndCommandValidator : AbstractValidator<IngestSessionEndCommand>
{
    public IngestSessionEndCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.EventId).MaximumLength(64);
        RuleFor(x => x.EndedAt).NotEmpty();
    }
}
