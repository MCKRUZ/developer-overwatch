namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using FluentValidation;

public sealed class IngestSessionStartCommandValidator : AbstractValidator<IngestSessionStartCommand>
{
    public IngestSessionStartCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.EventId).MaximumLength(64);
        RuleFor(x => x.DeveloperEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.StartedAt).NotEmpty();
        RuleFor(x => x.Agent).MaximumLength(64);
        RuleFor(x => x.MachineName).MaximumLength(255);
        RuleFor(x => x.ProjectKey)
            .MaximumLength(512)
            // The Agent sends a normalized "host/path" key (git@host:o/r becomes host/o/r), so a
            // key never needs '@'. Any '@' means userinfo, possibly a credential, slipped through.
            .Must(key => key is null || !key.Contains('@'))
            .WithMessage("ProjectKey must not contain '@'.");
    }
}
