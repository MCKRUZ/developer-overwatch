namespace DeveloperOverwatch.Application.Queries.GetPricing;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPricingQueryHandler : IRequestHandler<GetPricingQuery, Result<PricingInfo>>
{
    private readonly ITokenPricingCalculator _pricing;

    public GetPricingQueryHandler(ITokenPricingCalculator pricing) => _pricing = pricing;

    public Task<Result<PricingInfo>> Handle(GetPricingQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<PricingInfo>.Success(new PricingInfo(_pricing.PricesAsOf)));
}
