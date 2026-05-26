namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPrWebhookCommand : IRequest<Result<int>>
{
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public required string Action { get; init; }
}
