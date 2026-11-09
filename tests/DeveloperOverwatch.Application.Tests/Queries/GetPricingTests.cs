namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetPricing;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetPricingTests
{
    [Fact]
    public async Task Handle_ReturnsTheConfiguredPricesAsOfDate()
    {
        var pricing = Substitute.For<ITokenPricingCalculator>();
        pricing.PricesAsOf.Returns(new DateOnly(2026, 11, 9));

        var result = await new GetPricingQueryHandler(pricing).Handle(new GetPricingQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new PricingInfo(new DateOnly(2026, 11, 9)));
    }
}
