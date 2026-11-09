using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Sends one ToolExecution or FileChange event and settles it by its items' answers: done when every
/// item is accepted or a duplicate, dead when any is rejected, transient while any is unanswered. A refused
/// request is split until each item has its own answer (see <see cref="HalvingPoster"/>).</summary>
internal sealed class ToolEventShipper(IApiClient api, IPrivacyRedactor redactor, ILogger logger)
{
    private sealed record Answer(string? Id, ItemOutcome? Outcome, string? Reason);

    public Task<EventOutcome> ShipAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var payload = AgentPayload.NormalizeToolEvent(redactor.Redact(evt.PayloadJson));
        var body = AgentPayload.ParseObject(payload);
        var items = body is null ? [] : AgentPayload.ListItems(body);
        // With no items to split, the payload goes as it is, one unit.
        return items.Count == 0
            ? PostAsync(sessionId, evt, [payload], units => units[0], _ => null, ct)
            : PostAsync(sessionId, evt, items, units => AgentPayload.WithItems(body!, units), item => item.Id, ct);
    }

    private async Task<EventOutcome> PostAsync<T>(
        string sessionId, QueuedEvent evt, IReadOnlyList<T> units, Func<IReadOnlyList<T>, string> build,
        Func<T, string?> idOf, CancellationToken ct)
    {
        var answers = new List<Answer>();
        var stop = await HalvingPoster.PostAsync(units, new HalvingPoster.Callbacks<T>(
            subset => api.SendSessionEventsAsync(sessionId, build(subset), ct),
            (subset, result) =>
            {
                answers.AddRange(subset.Select(u => idOf(u) is { } id
                    ? new Answer(id, result.OutcomeOf(id), result.RejectionReason(id))
                    : new Answer(null, ItemOutcome.Accepted, null)));
                return Task.CompletedTask;
            },
            (unit, result) =>
            {
                answers.Add(new Answer(idOf(unit), ItemOutcome.Rejected, result.Reason));
                return Task.CompletedTask;
            },
            unit => logger.LogWarning(
                "Server refused item {ItemId} of session {SessionId} as too large even alone; holding it for the next poll",
                idOf(unit) ?? evt.Id, sessionId)));

        return stop.ToOutcome(Settle(sessionId, evt, answers));
    }

    private EventOutcome Settle(string sessionId, QueuedEvent evt, IReadOnlyList<Answer> answers)
    {
        var unanswered = answers.Where(a => a.Outcome is null).Select(a => a.Id).ToList();
        if (unanswered.Count > 0)
        {
            logger.LogWarning("Server's results left out items {ItemIds} of session {SessionId}; retrying them",
                string.Join(", ", unanswered), sessionId);
            return EventOutcome.Transient;
        }

        var rejected = answers.Where(a => a.Outcome == ItemOutcome.Rejected).ToList();
        foreach (var answer in rejected)
            logger.LogWarning("Server rejected item {ItemId} of session {SessionId}; dropping it: {Reason}",
                answer.Id ?? evt.Id, sessionId, answer.Reason);
        return rejected.Count > 0 ? EventOutcome.Dead : EventOutcome.Done;
    }
}
