namespace DeveloperOverwatch.API.Hubs;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;

public sealed class SignalROverwatchNotifier : IOverwatchNotifier
{
    private readonly IHubContext<OverwatchHub, IOverwatchHubClient> _hub;

    public SignalROverwatchNotifier(IHubContext<OverwatchHub, IOverwatchHubClient> hub) =>
        _hub = hub;

    public async Task ViolationCreatedAsync(GovernanceViolation violation, CancellationToken ct)
    {
        var dto = new ViolationDto(
            violation.Id,
            violation.SessionId,
            violation.RuleId,
            violation.Severity.ToString(),
            violation.Message);

        await Task.WhenAll(
            _hub.Clients.Group(violation.SessionId).ViolationCreated(dto),
            _hub.Clients.All.ViolationCreated(dto));
    }
}
