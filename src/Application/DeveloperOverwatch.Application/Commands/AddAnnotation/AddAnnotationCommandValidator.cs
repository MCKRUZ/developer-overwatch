namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using FluentValidation;

public sealed class AddAnnotationCommandValidator : AbstractValidator<AddAnnotationCommand>
{
    public AddAnnotationCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.CreatedBy).NotEmpty().EmailAddress();
    }
}
