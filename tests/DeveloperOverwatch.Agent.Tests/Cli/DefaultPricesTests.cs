using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Cli;

public sealed class DefaultPricesTests
{
    [Fact]
    public void Defaults_EqualTheServersTokenPricing()
    {
        using var appsettings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "server-appsettings.json")));
        var server = PricingSection.Read(appsettings.RootElement.GetProperty("TokenPricing"));

        var defaults = PricingSection.ReadDefaults();

        defaults.AsOf.Should().Be(server.AsOf);
        defaults.Models!.Keys.Should().BeEquivalentTo(server.Models!.Keys);
        foreach (var (model, rates) in server.Models)
            defaults.Models[model]!.ToModelRates(model).Should().Be(rates!.ToModelRates(model), because: model);
    }

    // Guards the parity test above: if the shared reader dropped the tier, both sides would lose it equally.
    [Fact]
    public void Defaults_KeepTheHaikuLongPromptTier()
    {
        var haiku = PricingSection.ReadDefaults().ToPriceTable().RatesFor("claude-haiku-5-5");

        haiku.LongPrompt.Should().NotBeNull();
        haiku.LongPrompt!.ThresholdTokens.Should().Be(100_000);
        haiku.LongPrompt.Rates.InputPerMillionUsd.Should().Be(0.5m);
    }

    [Fact]
    public void PilotConfig_WithMalformedPricingAndBudget_StillLoadsAsPilot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ow-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "apiUrl": "http://example.test", "pricing": "garbage", "budget": 5 }""");
        try
        {
            var config = AgentConfig.Load(path);

            config.Mode.Should().Be(AgentMode.Pilot, "a typo in a usage section must not stop a pilot machine shipping");
            config.Pricing!.Value.ValueKind.Should().Be(JsonValueKind.String);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
