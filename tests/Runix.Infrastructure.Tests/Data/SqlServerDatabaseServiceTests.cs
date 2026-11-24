using System;
using Microsoft.Data.SqlClient;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Factories;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class SqlServerDatabaseServiceTests
{
    [Fact]
    public void QuoteIdentifier_HandlesSingleAndQualifiedIdentifiers()
    {
        Assert.Equal("[users]", SqlServerDatabaseService.QuoteIdentifier("users"));
        Assert.Equal("[dbo].[orders]", SqlServerDatabaseService.QuoteIdentifier("dbo.orders"));
        Assert.Equal("[sales].[weird]]name]", SqlServerDatabaseService.QuoteIdentifier("sales.weird]name"));
        Assert.Equal("", SqlServerDatabaseService.QuoteIdentifier(""));
    }

    [Fact]
    public void BuildConnectionString_HandlesCustomPortAndTrustServerCertificate()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "sqlserver.corp",
            Port = 14333,
            Database = "enterprise",
            Username = "sa",
            Password = "StrongPassword!",
            TrustServerCertificate = true,
            SslMode = "Require"
        };

        var service = new SqlServerDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new SqlConnectionStringBuilder(connStr);
        Assert.Equal("sqlserver.corp,14333", builder.DataSource);
        Assert.Equal("enterprise", builder.InitialCatalog);
        Assert.Equal("sa", builder.UserID);
        Assert.Equal("StrongPassword!", builder.Password);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
    }

    [Fact]
    public void BuildConnectionString_DefaultPort_OmitsPortSuffix()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "localhost",
            Port = 1433,
            Database = "master",
            Username = "sa",
            Password = "password",
            TrustServerCertificate = false
        };

        var service = new SqlServerDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new SqlConnectionStringBuilder(connStr);
        Assert.Equal("localhost", builder.DataSource);
        Assert.False(builder.TrustServerCertificate);
    }

    [Fact]
    public void BuildConnectionString_SupportsOverrideForSshTunnel()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "db-private.internal",
            Port = 1433,
            Database = "prod",
            Username = "app",
            Password = "safe",
            UseSshTunnel = true,
            SshHost = "bastion.corp.com"
        };

        var service = new SqlServerDatabaseService(config);
        var tunnelConnStr = service.BuildConnectionString(overridePort: 54322, overrideHost: "127.0.0.1");

        var builder = new SqlConnectionStringBuilder(tunnelConnStr);
        Assert.Equal("127.0.0.1,54322", builder.DataSource);
        Assert.Equal("prod", builder.InitialCatalog);
    }

    [Fact]
    public void DatabaseServiceFactory_CreatesSqlServerService()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "localhost",
            Database = "master"
        };

        using var service = DatabaseServiceFactory.Create(config);
        Assert.IsType<SqlServerDatabaseService>(service);
    }
}
