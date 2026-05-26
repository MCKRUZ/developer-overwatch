namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using FluentValidation;

public sealed class IngestSessionEventsCommandValidator : AbstractValidator<IngestSessionEventsCommand>
{
    public IngestSessionEventsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleForEach(x => x.ToolExecutions).ChildRules(tool =>
        {
            tool.RuleFor(t => t.Id).NotEmpty();
            tool.RuleFor(t => t.ToolName).NotEmpty();
        });

        RuleForEach(x => x.FileChanges).ChildRules(file =>
        {
            file.RuleFor(f => f.Id).NotEmpty();
            file.RuleFor(f => f.FilePath).NotEmpty();
        });

        RuleForEach(x => x.ConversationTurns).ChildRules(turn =>
        {
            turn.RuleFor(t => t.Id).NotEmpty();
            turn.RuleFor(t => t.Role).NotEmpty();
        });
    }
}
