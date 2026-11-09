namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using FluentValidation;

/// <summary>Request-level rules only: a failure here rejects the whole batch with a 400. Rules about
/// a single item live in <see cref="IngestItemRules"/>, so one bad item is reported on its own and
/// the rest of the batch is still stored.</summary>
public sealed class IngestSessionEventsCommandValidator : AbstractValidator<IngestSessionEventsCommand>
{
    private const int MaxItemsPerList = 500;

    public IngestSessionEventsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleFor(x => x.ConversationTurns).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} conversation turns per batch.");
        RuleFor(x => x.ToolExecutions).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} tool executions per batch.");
        RuleFor(x => x.FileChanges).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} file changes per batch.");
        RuleFor(x => x.ActivityEvents).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} activity events per batch.");
        RuleFor(x => x.ToolFailures).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} tool failures per batch.");
    }
}
