namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using FluentValidation;

public sealed class IngestSessionStartCommandValidator : AbstractValidator<IngestSessionStartCommand>
{
    public IngestSessionStartCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.DeveloperEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.StartedAt).NotEmpty();
    }
}
