using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class DatabaseTypeAndConfigTests
{
    [Fact]
    public void DatabaseType_ContainsAllExpectedEngines()
    {
        Assert.Equal(0, (int)DatabaseType.SQLite);
        Assert.Equal(1, (int)DatabaseType.PostgreSQL);
        Assert.True(System.Enum.IsDefined(typeof(DatabaseType), DatabaseType.MySQL));
        Assert.True(System.Enum.IsDefined(typeof(DatabaseType), DatabaseType.MariaDB));
        Assert.True(System.Enum.IsDefined(typeof(DatabaseType), DatabaseType.SqlServer));
    }

    [Fact]
    public void ConnectionConfig_MySql_FormatsSubtitleAndIconCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MySQL,
            Host = "db.internal",
            Port = 3306,
            Database = "shop",
            Username = "appuser"
        };

        Assert.Equal("🐬", config.EngineIcon);
        Assert.Equal("appuser@db.internal:3306/shop", config.DisplaySubtitle);
        Assert.Equal("mysql:db.internal:3306:shop:appuser", config.GetSessionKey());
    }

    [Fact]
    public void ConnectionConfig_MariaDb_FormatsSubtitleAndIconCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.MariaDB,
            Host = "mariadb-node",
            Port = 3307,
            Database = "billing",
            Username = "root"
        };

        Assert.Equal("🦭", config.EngineIcon);
        Assert.Equal("root@mariadb-node:3307/billing", config.DisplaySubtitle);
        Assert.Equal("mariadb:mariadb-node:3307:billing:root", config.GetSessionKey());
    }

    [Fact]
    public void ConnectionConfig_SqlServer_FormatsSubtitleAndIconCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "sql.corp",
            Port = 1433,
            Database = "finance",
            Username = "sa",
            DefaultSchema = "sales",
            TrustServerCertificate = true
        };

        Assert.Equal("🏢", config.EngineIcon);
        Assert.Equal("sa@sql.corp:1433/finance (sales)", config.DisplaySubtitle);
        Assert.Equal("sqlserver:sql.corp:1433:finance:sa", config.GetSessionKey());
        Assert.True(config.TrustServerCertificate);
    }

    [Fact]
    public void ConnectionConfig_Clone_CopiesAllMultiEngineProperties()
    {
        var original = new ConnectionConfig
        {
            Type = DatabaseType.SqlServer,
            Host = "sql01",
            Port = 14333,
            Database = "crm",
            Username = "admin",
            Password = "secret!",
            DefaultSchema = "reporting",
            TrustServerCertificate = false,
            UseSshTunnel = true,
            SshHost = "bastion.corp"
        };

        var clone = original.Clone();

        Assert.Equal(original.Type, clone.Type);
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Database, clone.Database);
        Assert.Equal(original.Username, clone.Username);
        Assert.Equal(original.Password, clone.Password);
        Assert.Equal(original.DefaultSchema, clone.DefaultSchema);
        Assert.Equal(original.TrustServerCertificate, clone.TrustServerCertificate);
        Assert.Equal(original.UseSshTunnel, clone.UseSshTunnel);
        Assert.Equal(original.SshHost, clone.SshHost);
    }
}
