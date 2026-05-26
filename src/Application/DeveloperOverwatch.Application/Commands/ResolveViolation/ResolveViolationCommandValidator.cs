namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using FluentValidation;

public sealed class ResolveViolationCommandValidator : AbstractValidator<ResolveViolationCommand>
{
    public ResolveViolationCommandValidator()
    {
        RuleFor(x => x.ViolationId).NotEmpty();
        RuleFor(x => x.ResolvedBy).NotEmpty().EmailAddress();
        RuleFor(x => x.ResolutionReason).NotEmpty().MaximumLength(1000);
    }
}
