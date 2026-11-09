namespace DeveloperOverwatch.Application.Services;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// The single unkeyed <see cref="IOverwatchNotifier"/>. Fans each violation out to every
/// notification channel registered under <see cref="ChannelKey"/> (Slack/email, SignalR, ...).
/// A failing channel is logged and never stops the others.
/// </summary>
public sealed class CompositeOverwatchNotifier : IOverwatchNotifier
{
    /// <summary>Register each channel with <c>AddKeyedScoped&lt;IOverwatchNotifier, T&gt;(ChannelKey)</c>.</summary>
    public const string ChannelKey = "overwatch-notifier-channel";

    private readonly IReadOnlyList<IOverwatchNotifier> _channels;
    private readonly ILogger<CompositeOverwatchNotifier> _logger;

    public CompositeOverwatchNotifier(
        [FromKeyedServices(ChannelKey)] IEnumerable<IOverwatchNotifier> channels,
        ILogger<CompositeOverwatchNotifier> logger)
    {
        _channels = channels.ToArray();
        _logger = logger;
    }

    public Task ViolationCreatedAsync(GovernanceViolation violation, CancellationToken ct = default) =>
        Task.WhenAll(_channels.Select(channel => SafeNotifyAsync(channel, violation, ct)));

    private async Task SafeNotifyAsync(
        IOverwatchNotifier channel, GovernanceViolation violation, CancellationToken ct)
    {
        try
        {
            await channel.ViolationCreatedAsync(violation, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Notifier {Channel} failed for violation {RuleId} in session {SessionId}",
                channel.GetType().Name, violation.RuleId, violation.SessionId);
        }
    }
}
