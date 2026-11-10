using DeveloperOverwatch.Agent.Services;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Floor comparisons mix hook timestamps (unspecified kind, already UTC), transcript ones and queue ones.
/// One misread kind shifts the floor by the machine's offset and leaks or drops hours of turns.</summary>
public sealed class UtcInstantTests
{
    private static readonly DateTime Instant = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Utc_UtcKind_IsUnchanged()
    {
        var result = UtcInstant.Utc(Instant);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(Instant.Ticks);
    }

    [Fact]
    public void Utc_LocalKind_ConvertsToTheSameInstant()
    {
        var result = UtcInstant.Utc(Instant.ToLocalTime());

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(Instant.Ticks);
    }

    [Fact]
    public void Utc_UnspecifiedKind_IsTakenAsUtcWithoutShifting()
    {
        var result = UtcInstant.Utc(new DateTime(Instant.Ticks, DateTimeKind.Unspecified));

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(Instant.Ticks);
    }

    [Fact]
    public void Later_NullFirst_ReturnsTheSecondInUtc()
    {
        var result = UtcInstant.Later(null, Instant.ToLocalTime());

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(Instant.Ticks);
    }

    [Fact]
    public void Later_FirstIsLater_ReturnsTheFirst()
    {
        UtcInstant.Later(Instant.AddMinutes(1), Instant).Should().Be(Instant.AddMinutes(1));
    }

    [Fact]
    public void Later_SecondIsLater_ReturnsTheSecond()
    {
        UtcInstant.Later(Instant, Instant.AddMinutes(1)).Should().Be(Instant.AddMinutes(1));
    }

    [Fact]
    public void Later_MixedKinds_ComparesInstantsNotClockReadings()
    {
        var laterLocal = Instant.AddMinutes(1).ToLocalTime();

        var result = UtcInstant.Later(laterLocal, Instant);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(Instant.AddMinutes(1).Ticks);
    }
}
