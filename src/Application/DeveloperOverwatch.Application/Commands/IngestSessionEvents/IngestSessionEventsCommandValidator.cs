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
            turn.RuleFor(t => t.GitBranch).MaximumLength(255);
            turn.RuleFor(t => t.SubagentName).MaximumLength(255);
        });

        RuleForEach(x => x.ActivityEvents).ChildRules(activity =>
        {
            activity.RuleFor(a => a.State).IsInEnum();
            activity.RuleFor(a => a.At).NotEmpty();
        });

        RuleForEach(x => x.ToolFailures).ChildRules(failure =>
        {
            failure.RuleFor(f => f.ToolName).NotEmpty().MaximumLength(255);
            failure.RuleFor(f => f.At).NotEmpty();
        });
    }
}
