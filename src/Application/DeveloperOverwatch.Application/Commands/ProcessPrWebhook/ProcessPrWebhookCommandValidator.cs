namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using FluentValidation;

public sealed class ProcessPrWebhookCommandValidator : AbstractValidator<ProcessPrWebhookCommand>
{
    public ProcessPrWebhookCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.PrNumber).GreaterThan(0);
        RuleFor(x => x.Action).NotEmpty();
    }
}
