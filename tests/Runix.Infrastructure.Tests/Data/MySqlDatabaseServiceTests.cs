using System;
using System.Threading.Tasks;
using MySqlConnector;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Factories;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class MySqlDatabaseServiceTests
{
    [Fact]
    public void QuoteIdentifier_HandlesSingleAndQualifiedIdentifiers()
    {
        Assert.Equal("`users`", MySqlDatabaseService.QuoteIdentifier("users"));
        Assert.Equal("`shop`.`orders`", MySqlDatabaseService.QuoteIdentifier("shop.orders"));
        Assert.Equal("`weird``name`", MySqlDatabaseService.QuoteIdentifier("weird`name"));
        Assert.Equal("", MySqlDatabaseService.QuoteIdentifier(""));
    }

    [Theory]
    [InlineData("Disable", MySqlSslMode.None)]
    [InlineData("None", MySqlSslMode.None)]
    [InlineData("Require", MySqlSslMode.Required)]
    [InlineData("Required", MySqlSslMode.Required)]
    [InlineData("Prefer", MySqlSslMode.Preferred)]
    public void BuildConnectionString_RespectsSslModes(string sslModeInput, MySqlSslMode expectedSslMode)
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MySQL,
            Host = "mysql-host",
            Port = 3306,
            Database = "testdb",
            Username = "user",
            Password = "pw",
            SslMode = sslModeInput
        };

        var service = new MySqlDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new MySqlConnectionStringBuilder(connStr);
        Assert.Equal("mysql-host", builder.Server);
        Assert.Equal(3306u, builder.Port);
        Assert.Equal("testdb", builder.Database);
        Assert.Equal("user", builder.UserID);
        Assert.Equal("pw", builder.Password);
        Assert.Equal(expectedSslMode, builder.SslMode);
        Assert.Equal("utf8mb4", builder.CharacterSet);
    }

    [Fact]
    public void BuildConnectionString_SupportsOverrideForSshTunnel()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MySQL,
            Host = "remote.internal",
            Port = 3306,
            Database = "prod",
            Username = "app",
            Password = "safe",
            UseSshTunnel = true,
            SshHost = "ssh.corp.com"
        };

        var service = new MySqlDatabaseService(config);
        var tunnelConnStr = service.BuildConnectionString(overridePort: 54321, overrideHost: "127.0.0.1");

        var builder = new MySqlConnectionStringBuilder(tunnelConnStr);
        Assert.Equal("127.0.0.1", builder.Server);
        Assert.Equal(54321u, builder.Port);
        Assert.Equal("prod", builder.Database);
    }

    [Fact]
    public void DatabaseServiceFactory_CreatesMySqlService()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MySQL,
            Host = "localhost",
            Database = "app"
        };

        using var service = DatabaseServiceFactory.Create(config);
        Assert.IsType<MySqlDatabaseService>(service);
    }

    [Fact]
    public void DatabaseServiceFactory_CreatesMariaDbService()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MariaDB,
            Host = "localhost",
            Database = "app"
        };

        using var service = DatabaseServiceFactory.Create(config);
        Assert.IsType<MySqlDatabaseService>(service);
    }
}
