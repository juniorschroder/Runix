using System;
using System.IO;
using Runix.Core.Models;
using Runix.Infrastructure.Resilience;
using Xunit;

namespace Runix.Infrastructure.Tests.Resilience;

public class OracleResiliencePolicyTests
{
    [Fact]
    public void IsTransientException_DetectsTimeoutAndIoException()
    {
        Assert.True(ResiliencePolicyFactory.IsTransientException(new TimeoutException("Oracle socket timeout")));
        Assert.True(ResiliencePolicyFactory.IsTransientException(new IOException("Oracle connection dropped")));
        Assert.False(ResiliencePolicyFactory.IsTransientException(new InvalidOperationException("Invalid SQL syntax")));
    }

    [Fact]
    public void CreateRetryPolicy_ForOracle_CreatesValidVardPolicy()
    {
        int retryCalls = 0;
        var policy = ResiliencePolicyFactory.CreateRetryPolicy(
            DatabaseType.Oracle,
            onRetry: (ex, attempt, delay) => retryCalls++);

        Assert.NotNull(policy);
    }

    [Fact]
    public void CreateCircuitBreakerPolicy_ForOracle_CreatesValidVardPolicy()
    {
        var cbPolicy = ResiliencePolicyFactory.CreateCircuitBreakerPolicy(DatabaseType.Oracle);
        Assert.NotNull(cbPolicy);
    }
}
