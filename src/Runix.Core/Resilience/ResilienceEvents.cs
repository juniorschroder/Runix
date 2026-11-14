using System;

namespace Runix.Core.Resilience;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public enum ResilienceEventType
{
    RetryAttempt,
    CircuitOpen,
    CircuitClosed,
    CircuitHalfOpen
}

public record ResilienceEvent(
    ResilienceEventType Type,
    string DatabaseTarget,
    int Attempt = 0,
    int MaxAttempts = 0,
    TimeSpan Delay = default,
    string? Reason = null
);
