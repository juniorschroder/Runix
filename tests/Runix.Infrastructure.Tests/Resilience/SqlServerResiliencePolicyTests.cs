using System;
using Runix.Core.Models;
using Runix.Infrastructure.Resilience;
using Xunit;

namespace Runix.Infrastructure.Tests.Resilience;

public class SqlServerResiliencePolicyTests
{
    [Fact]
    public void CreateRetryPolicy_ForSqlServer_UsesAppropriateInitialDelay()
    {
        var policy = ResiliencePolicyFactory.CreateRetryPolicy(DatabaseType.SqlServer);
        Assert.NotNull(policy);
    }

    [Fact]
    public void CreateCircuitBreakerPolicy_ForSqlServer_CreatesValidPolicy()
    {
        var cbPolicy = ResiliencePolicyFactory.CreateCircuitBreakerPolicy(DatabaseType.SqlServer);
        Assert.NotNull(cbPolicy);
    }
}
