namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using FluentValidation;

public sealed class IngestSessionEventsCommandValidator : AbstractValidator<IngestSessionEventsCommand>
{
    private readonly TimeProvider _time;

    public IngestSessionEventsCommandValidator(TimeProvider time)
    {
        _time = time;
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

        RuleFor(x => x.ActivityEvents).Must(e => e.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} activity events per batch.");
        RuleFor(x => x.ToolFailures).Must(f => f.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} tool failures per batch.");

        // Only a newer timestamp replaces the stored state, so a far-future one would pin it forever.
        RuleForEach(x => x.ActivityEvents).ChildRules(activity =>
        {
            activity.RuleFor(a => a.State).IsInEnum();
            activity.RuleFor(a => a.At).NotEmpty().Must(NotInTheFuture).WithMessage(FutureMessage);
        });

        RuleForEach(x => x.ToolFailures).ChildRules(failure =>
        {
            failure.RuleFor(f => f.Id).NotEmpty().MaximumLength(64);
            failure.RuleFor(f => f.ToolName).NotEmpty().MaximumLength(255);
            failure.RuleFor(f => f.At).NotEmpty().Must(NotInTheFuture).WithMessage(FutureMessage);
        });
    }

    private const int MaxItemsPerList = 500;
    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(5);
    private const string FutureMessage = "Timestamp must not be more than 5 minutes in the future.";

    private bool NotInTheFuture(DateTime at) => at <= _time.GetUtcNow().UtcDateTime + ClockSkewAllowance;
}
