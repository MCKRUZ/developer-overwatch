namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using System.Text.RegularExpressions;
using FluentValidation;

public sealed class IngestSessionStartCommandValidator : AbstractValidator<IngestSessionStartCommand>
{
    // A URL with userinfo ("https://user:token@host/..."). The Agent strips credentials before
    // sending; this rejects any that slip through so they are never stored.
    private static readonly Regex CredentialUrl = new(
        "^[a-z][a-z0-9+.-]*://[^/]*@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public IngestSessionStartCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.DeveloperEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.StartedAt).NotEmpty();
        RuleFor(x => x.Agent).MaximumLength(64);
        RuleFor(x => x.MachineName).MaximumLength(255);
        RuleFor(x => x.ProjectKey)
            .MaximumLength(512)
            .Must(key => key is null || !CredentialUrl.IsMatch(key))
            .WithMessage("ProjectKey must not contain URL credentials.");
    }
}
