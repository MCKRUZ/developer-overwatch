namespace DeveloperOverwatch.Application.Queries.GetPricing;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetPricingQuery : IRequest<Result<PricingInfo>>;

/// <param name="AsOf">Date the configured token prices were taken from the published price list.</param>
public sealed record PricingInfo(DateOnly AsOf);
