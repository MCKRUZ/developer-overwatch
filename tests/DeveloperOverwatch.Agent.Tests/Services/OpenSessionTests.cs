using DeveloperOverwatch.Agent.Services;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class OpenSessionTests
{
    private static readonly DateTime Floor = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Admits_Unbounded_AdmitsEverythingIncludingUndated()
    {
        var session = new OpenSession("s1", DateTime.MinValue);

        session.Bounded.Should().BeFalse();
        session.Admits(null).Should().BeTrue();
        session.Admits(Floor.AddYears(-10)).Should().BeTrue();
    }

    [Fact]
    public void Admits_AtTheFloor_IsAdmitted()
    {
        new OpenSession("s1", Floor).Admits(Floor).Should().BeTrue();
    }

    [Fact]
    public void Admits_BelowTheFloor_IsNotAdmitted()
    {
        new OpenSession("s1", Floor).Admits(Floor.AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void Admits_Undated_IsNotAdmittedByABoundedSession()
    {
        new OpenSession("s1", Floor).Admits(null).Should().BeFalse();
    }

    [Fact]
    public void Admits_LocalKindAtTheFloor_IsComparedAsTheSameInstant()
    {
        new OpenSession("s1", Floor).Admits(Floor.ToLocalTime()).Should().BeTrue();
    }

    [Fact]
    public void Floor_LocalKind_IsNormalisedToUtc()
    {
        var session = new OpenSession("s1", Floor.ToLocalTime());

        session.Floor.Kind.Should().Be(DateTimeKind.Utc);
        session.Floor.Ticks.Should().Be(Floor.Ticks);
    }
}
