using System;
using System.IO;
using Runix.Core.Models;
using Runix.Infrastructure.Resilience;
using Xunit;

namespace Runix.Infrastructure.Tests.Resilience;

public class MySqlResiliencePolicyTests
{
    [Fact]
    public void IsTransientException_DetectsTimeoutAndIoException()
    {
        Assert.True(ResiliencePolicyFactory.IsTransientException(new TimeoutException("Timeout connecting")));
        Assert.True(ResiliencePolicyFactory.IsTransientException(new IOException("Socket broke")));
        Assert.False(ResiliencePolicyFactory.IsTransientException(new InvalidOperationException("Bad query")));
    }

    [Fact]
    public void CreateRetryPolicy_ForMySql_CreatesValidVardPolicy()
    {
        int retryCalls = 0;
        var policy = ResiliencePolicyFactory.CreateRetryPolicy(
            DatabaseType.MySQL,
            onRetry: (ex, attempt, delay) => retryCalls++);

        Assert.NotNull(policy);
    }

    [Fact]
    public void CreateCircuitBreakerPolicy_ForMariaDb_CreatesValidVardPolicy()
    {
        var cbPolicy = ResiliencePolicyFactory.CreateCircuitBreakerPolicy(DatabaseType.MariaDB);
        Assert.NotNull(cbPolicy);
    }
}
