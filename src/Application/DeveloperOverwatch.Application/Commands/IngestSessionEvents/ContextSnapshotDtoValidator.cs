namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using FluentValidation;

/// <summary>Every bound the stored snapshot's columns and the dashboard rely on. A failure refuses the whole
/// request; the Agent then resends the same turns without the snapshot.</summary>
public sealed class ContextSnapshotDtoValidator : AbstractValidator<ContextSnapshotDto>
{
    public ContextSnapshotDtoValidator()
    {
        RuleFor(s => s.TakenAt).NotEqual(default(DateTime));
        RuleFor(s => s.ClaudeCodeVersion).NotEmpty().MaximumLength(ContextSnapshotLimits.ClaudeCodeVersionLength);
        RuleFor(s => s.Model).NotEmpty().MaximumLength(ContextSnapshotLimits.NameLength);
        RuleFor(s => s.MeasuredContextTokens).GreaterThanOrEqualTo(0);

        RuleFor(s => s.Categories).Cascade(CascadeMode.Stop).NotNull()
            .Must(l => l.Count <= ContextSnapshotLimits.MaxCategories)
            .WithMessage($"At most {ContextSnapshotLimits.MaxCategories} context categories.");
        RuleForEach(s => s.Categories).NotNull().ChildRules(c =>
        {
            c.RuleFor(x => x.Category).IsInEnum();
            c.RuleFor(x => x.Server).MaximumLength(ContextSnapshotLimits.NameLength);
            c.RuleFor(x => x.Tokens).GreaterThanOrEqualTo(0);
        });

        RuleFor(s => s.Tools).Cascade(CascadeMode.Stop).NotNull()
            .Must(l => l.Count <= ContextSnapshotLimits.MaxTools)
            .WithMessage($"At most {ContextSnapshotLimits.MaxTools} tool groups.");
        RuleForEach(s => s.Tools).NotNull().ChildRules(t =>
        {
            t.RuleFor(x => x.Group).NotEmpty().MaximumLength(ContextSnapshotLimits.NameLength);
            t.RuleFor(x => x.ToolCount).GreaterThanOrEqualTo(0);
            t.RuleFor(x => x.TokensPerRequest).GreaterThanOrEqualTo(0);
        });

        RuleFor(s => s.Compactions).Cascade(CascadeMode.Stop).NotNull()
            .Must(l => l.Count <= ContextSnapshotLimits.MaxCompactions)
            .WithMessage($"At most {ContextSnapshotLimits.MaxCompactions} compactions.");
        RuleForEach(s => s.Compactions).NotNull().ChildRules(c =>
        {
            c.RuleFor(x => x.At).NotEqual(default(DateTime));
            c.RuleFor(x => x.Trigger).NotEmpty().MaximumLength(ContextSnapshotLimits.TriggerLength);
            c.RuleFor(x => x.PreTokens).GreaterThanOrEqualTo(0);
            c.RuleFor(x => x.PostTokens).GreaterThanOrEqualTo(0);
        });
    }
}
