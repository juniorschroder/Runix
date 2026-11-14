using System;
using FluentAssertions;
using Runix.Core.Resilience;
using Xunit;

namespace Runix.Core.Tests.Resilience;

public class ResilienceEventsTests
{
    [Fact]
    public void ResilienceEvent_ConstructsCorrectly_WithProvidedParameters()
    {
        var ev = new ResilienceEvent(
            Type: ResilienceEventType.RetryAttempt,
            DatabaseTarget: "demo.db",
            Attempt: 2,
            MaxAttempts: 5,
            Delay: TimeSpan.FromMilliseconds(500),
            Reason: "database is locked");

        ev.Type.Should().Be(ResilienceEventType.RetryAttempt);
        ev.DatabaseTarget.Should().Be("demo.db");
        ev.Attempt.Should().Be(2);
        ev.MaxAttempts.Should().Be(5);
        ev.Delay.Should().Be(TimeSpan.FromMilliseconds(500));
        ev.Reason.Should().Be("database is locked");
    }

    [Fact]
    public void CircuitState_EnumValues_ContainExpectedStates()
    {
        Enum.GetNames<CircuitState>().Should().Contain(new[] { "Closed", "Open", "HalfOpen" });
    }

    [Fact]
    public void ResilienceEventType_EnumValues_ContainExpectedEvents()
    {
        Enum.GetNames<ResilienceEventType>().Should().Contain(new[]
        {
            "RetryAttempt",
            "CircuitOpen",
            "CircuitClosed",
            "CircuitHalfOpen"
        });
    }
}
