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
        Assert.True(System.Enum.IsDefined(typeof(DatabaseType), DatabaseType.Oracle));
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

    [Fact]
    public void ConnectionConfig_Oracle_ServiceName_FormatsSubtitleAndIconCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "oracle.corp",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.ServiceName,
            OracleServiceName = "FREEPDB1",
            Username = "system",
            OraclePrivilege = OraclePrivilege.Normal
        };

        Assert.Equal("🔴", config.EngineIcon);
        Assert.Equal("system@oracle.corp:1521/FREEPDB1", config.DisplaySubtitle);
        Assert.Equal("oracle:oracle.corp:1521:freepdb1:system", config.GetSessionKey());
    }

    [Fact]
    public void ConnectionConfig_Oracle_SidAndSysDba_FormatsSubtitleCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "localhost",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.Sid,
            OracleSid = "XE",
            Username = "sys",
            OraclePrivilege = OraclePrivilege.SysDba
        };

        Assert.Equal("sys@localhost:1521/XE [SYSDBA]", config.DisplaySubtitle);
        Assert.Equal("oracle:localhost:1521:xe:sys", config.GetSessionKey());
    }

    [Fact]
    public void ConnectionConfig_Oracle_WithSshTunnel_IncludesLockPrefix()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "db-private",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.ServiceName,
            OracleServiceName = "ORCLPDB1",
            Username = "scott",
            UseSshTunnel = true,
            SshHost = "gateway.corp"
        };

        Assert.Equal("🔒 SSH (gateway.corp) → scott@db-private:1521/ORCLPDB1", config.DisplaySubtitle);
    }

    [Fact]
    public void ConnectionConfig_Clone_CopiesOracleProperties()
    {
        var original = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "ora19c",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.Sid,
            OracleSid = "PROD1",
            OracleServiceName = "SRV_PROD",
            OracleTnsDescriptor = "(DESCRIPTION=...)",
            OraclePrivilege = OraclePrivilege.SysOper
        };

        var clone = original.Clone();

        Assert.Equal(DatabaseType.Oracle, clone.Type);
        Assert.Equal(OracleConnectionType.Sid, clone.OracleConnectionType);
        Assert.Equal("PROD1", clone.OracleSid);
        Assert.Equal("SRV_PROD", clone.OracleServiceName);
        Assert.Equal("(DESCRIPTION=...)", clone.OracleTnsDescriptor);
        Assert.Equal(OraclePrivilege.SysOper, clone.OraclePrivilege);
    }
}
