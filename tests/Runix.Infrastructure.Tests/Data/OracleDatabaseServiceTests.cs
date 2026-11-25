using System;
using Oracle.ManagedDataAccess.Client;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Factories;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class OracleDatabaseServiceTests
{
    [Fact]
    public void QuoteIdentifier_HandlesSingleAndQualifiedIdentifiers()
    {
        Assert.Equal("\"USERS\"", OracleDatabaseService.QuoteIdentifier("USERS"));
        Assert.Equal("\"HR\".\"EMPLOYEES\"", OracleDatabaseService.QuoteIdentifier("HR.EMPLOYEES"));
        Assert.Equal("\"SCOTT\".\"DEPT\"", OracleDatabaseService.QuoteIdentifier("SCOTT.DEPT"));
        Assert.Equal("", OracleDatabaseService.QuoteIdentifier(""));
    }

    [Fact]
    public void BuildConnectionString_ServiceName_FormatsCorrectDataSource()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "oracle-cluster.corp",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.ServiceName,
            OracleServiceName = "FREEPDB1",
            Username = "c##appuser",
            Password = "SecurePassword123!",
            OraclePrivilege = OraclePrivilege.Normal
        };

        var service = new OracleDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new OracleConnectionStringBuilder(connStr);
        Assert.Equal("oracle-cluster.corp:1521/FREEPDB1", builder.DataSource);
        Assert.Equal("c##appuser", builder.UserID);
        Assert.Equal("SecurePassword123!", builder.Password);
        Assert.DoesNotContain("DBA Privilege", connStr);
        Assert.True(string.IsNullOrEmpty(builder["DBA Privilege"]?.ToString()));
    }

    [Fact]
    public void BuildConnectionString_SidAndSysDba_FormatsSidDataSourceAndPrivilege()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "10.0.0.50",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.Sid,
            OracleSid = "XE",
            Username = "sys",
            Password = "SysPassword!",
            OraclePrivilege = OraclePrivilege.SysDba
        };

        var service = new OracleDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new OracleConnectionStringBuilder(connStr);
        Assert.Contains("(SID=XE)", builder.DataSource);
        Assert.Contains("(HOST=10.0.0.50)", builder.DataSource);
        Assert.Contains("(PORT=1521)", builder.DataSource);
        Assert.Equal("sys", builder.UserID);
        Assert.Equal("SysPassword!", builder.Password);
        Assert.Equal("SYSDBA", builder["DBA Privilege"]);
    }

    [Fact]
    public void BuildConnectionString_TnsDescriptor_UsesRawDescriptor()
    {
        var tns = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=rac-scan)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=SALES_PDB)))";
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            OracleConnectionType = OracleConnectionType.TnsDescriptor,
            OracleTnsDescriptor = tns,
            Username = "sales_user",
            Password = "SecretPassword"
        };

        var service = new OracleDatabaseService(config);
        var connStr = service.BuildConnectionString();

        var builder = new OracleConnectionStringBuilder(connStr);
        Assert.Equal(tns, builder.DataSource);
        Assert.Equal("sales_user", builder.UserID);
    }

    [Fact]
    public void BuildConnectionString_WithOverridePortAndHost_UsesForwardedEndpoint()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "remote.oracle.corp",
            Port = 1521,
            OracleConnectionType = OracleConnectionType.ServiceName,
            OracleServiceName = "ORCLPDB1",
            Username = "scott",
            Password = "tiger"
        };

        var service = new OracleDatabaseService(config);
        var connStr = service.BuildConnectionString(overridePort: 54321, overrideHost: "127.0.0.1");

        var builder = new OracleConnectionStringBuilder(connStr);
        Assert.Equal("127.0.0.1:54321/ORCLPDB1", builder.DataSource);
    }

    [Fact]
    public void DatabaseServiceFactory_CreatesOracleDatabaseService()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "localhost",
            Port = 1521,
            OracleServiceName = "FREEPDB1"
        };

        var service = DatabaseServiceFactory.Create(config);
        Assert.NotNull(service);
        Assert.IsType<OracleDatabaseService>(service);
        Assert.Equal(DatabaseType.Oracle, service.Config.Type);
    }
}
