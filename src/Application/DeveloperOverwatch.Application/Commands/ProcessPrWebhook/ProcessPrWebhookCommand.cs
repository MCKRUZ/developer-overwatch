namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using DeveloperOverwatch.Application.Idempotency;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPrWebhookCommand : IRequest<Result<int>>, IIdempotentRequest
{
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public required string Action { get; init; }

    /// <summary>GitHub <c>X-GitHub-Delivery</c> id; stable across retry deliveries.</summary>
    public string? DeliveryId { get; init; }

    public string IdempotencyKey =>
        string.IsNullOrWhiteSpace(DeliveryId) ? string.Empty : $"pr-webhook:{DeliveryId}";
}
