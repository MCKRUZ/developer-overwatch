namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

/// <summary>The outcome of one events batch. <see cref="EventsIngested"/> counts what was newly
/// stored, as it always has. <see cref="Results"/> gives each turn, tool execution, file change and
/// tool failure its own outcome by id. Activity events have no id and are not listed.</summary>
public sealed record IngestSessionEventsResult(int EventsIngested, IngestItemResults Results);

public sealed record IngestItemResults(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<string> Duplicate,
    IReadOnlyList<RejectedItem> Rejected);

/// <summary>An item that is invalid and would be rejected again on every resend.</summary>
public sealed record RejectedItem(string Id, string Reason);
