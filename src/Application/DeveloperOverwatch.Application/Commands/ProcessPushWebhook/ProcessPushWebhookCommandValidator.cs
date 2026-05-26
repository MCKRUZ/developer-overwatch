namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using FluentValidation;

public sealed class ProcessPushWebhookCommandValidator : AbstractValidator<ProcessPushWebhookCommand>
{
    public ProcessPushWebhookCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.Commits).NotEmpty();
    }
}
