using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Runix.Core.Services;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Storage;

public class QueryParameterStorageTests
{
    [Fact]
    public void Storage_CanSaveAndReloadParametersIntoService()
    {
        var service1 = new QueryParameterService();
        service1.SaveHistory("customer_id", "42");
        service1.SaveHistory("customer_id", "105");
        service1.SaveHistory("start_date", "2026-01-01");

        QueryParameterStorage.SaveFromService(service1, new[] { "customer_id", "start_date" });

        var service2 = new QueryParameterService();
        QueryParameterStorage.LoadIntoService(service2);

        var custHistory = service2.GetHistory("customer_id");
        custHistory.Should().Contain("105");
        custHistory.Should().Contain("42");

        var dateHistory = service2.GetHistory("start_date");
        dateHistory.Should().Contain("2026-01-01");
    }
}
