namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>Configured context-window size per model, used to show how full a session's context is.</summary>
public interface IModelContextWindowProvider
{
    /// <summary>Date the configured sizes were taken from the published model list.</summary>
    DateOnly AsOf { get; }

    /// <summary>Context window in tokens for the model, or the default size when it has no entry.</summary>
    long ContextWindowTokensFor(string? model);
}
