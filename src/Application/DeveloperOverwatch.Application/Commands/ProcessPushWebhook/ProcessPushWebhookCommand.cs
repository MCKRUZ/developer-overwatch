namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using DeveloperOverwatch.Application.Idempotency;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPushWebhookCommand : IRequest<Result<int>>, IIdempotentRequest
{
    public required string Repo { get; init; }
    public required IReadOnlyList<PushCommitDto> Commits { get; init; }

    /// <summary>GitHub <c>X-GitHub-Delivery</c> id; stable across retry deliveries.</summary>
    public string? DeliveryId { get; init; }

    public string IdempotencyKey =>
        string.IsNullOrWhiteSpace(DeliveryId) ? string.Empty : $"push-webhook:{DeliveryId}";
}

public sealed record PushCommitDto
{
    public required string Sha { get; init; }
    public required string Message { get; init; }
    public required DateTime Timestamp { get; init; }
}
