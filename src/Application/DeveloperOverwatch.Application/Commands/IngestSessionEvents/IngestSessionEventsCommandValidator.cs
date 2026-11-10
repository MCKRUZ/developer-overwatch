namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using FluentValidation;

/// <summary>Request-level rules only: a failure here rejects the whole batch with a 400. Rules about
/// a single item live in <see cref="IngestItemRules"/>, so one bad item is reported on its own and
/// the rest of the batch is still stored.</summary>
public sealed class IngestSessionEventsCommandValidator : AbstractValidator<IngestSessionEventsCommand>
{
    private const int MaxItemsPerList = 500;

    // Agents older than turn chunking send a whole transcript's turns in one POST. A lower cap would
    // reject those deterministically, and the agent would drop them.
    private const int MaxCapturedItemsPerList = 5000;

    public IngestSessionEventsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleFor(x => x.ConversationTurns).Must(l => l.Count <= MaxCapturedItemsPerList)
            .WithMessage($"At most {MaxCapturedItemsPerList} conversation turns per batch.");
        RuleFor(x => x.ToolExecutions).Must(l => l.Count <= MaxCapturedItemsPerList)
            .WithMessage($"At most {MaxCapturedItemsPerList} tool executions per batch.");
        RuleFor(x => x.FileChanges).Must(l => l.Count <= MaxCapturedItemsPerList)
            .WithMessage($"At most {MaxCapturedItemsPerList} file changes per batch.");
        RuleFor(x => x.ActivityEvents).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} activity events per batch.");
        RuleFor(x => x.ToolFailures).Must(l => l.Count <= MaxItemsPerList)
            .WithMessage($"At most {MaxItemsPerList} tool failures per batch.");
    }
}
