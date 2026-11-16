using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class SqlSafetyServiceTests
{
    private readonly SqlSafetyService _safetyService = new();

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("select id, name from accounts where balance > 100")]
    [InlineData("WITH cte AS (SELECT 1 AS val) SELECT * FROM cte")]
    [InlineData("EXPLAIN SELECT * FROM users")]
    [InlineData("EXPLAIN QUERY PLAN SELECT * FROM users")]
    [InlineData("SHOW TABLES")]
    [InlineData("DESCRIBE users")]
    [InlineData("PRAGMA table_info('users')")]
    public void ReadOnlyMode_AllowsSafeReadQueries(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "ReadOnly Replica",
            IsReadOnly = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.True(result.IsValid);
        Assert.Equal(SqlSafetyRisk.Safe, result.Risk);
        Assert.Null(result.BlockedReason);
    }

    [Theory]
    [InlineData("INSERT INTO users (name) VALUES ('Junior')")]
    [InlineData("UPDATE users SET name = 'Junior' WHERE id = 1")]
    [InlineData("DELETE FROM users WHERE id = 1")]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("ALTER TABLE users ADD COLUMN email TEXT")]
    [InlineData("CREATE TABLE logs (id INT)")]
    [InlineData("REPLACE INTO users (id, name) VALUES (1, 'Junior')")]
    public void ReadOnlyMode_BlocksMutationsAndDdl(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "Protected Replica",
            IsReadOnly = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.False(result.IsValid);
        Assert.Equal(SqlSafetyRisk.BlockedReadOnly, result.Risk);
        Assert.NotNull(result.BlockedReason);
        Assert.Contains("Modo Somente-Leitura", result.BlockedReason);
    }

    [Fact]
    public void ReadOnlyMode_BlocksMultiStatementIfAnyStatementMutates()
    {
        var config = new ConnectionConfig
        {
            Name = "Protected DB",
            IsReadOnly = true
        };

        var sql = "SELECT * FROM users;\nDROP TABLE sessions;";
        var result = _safetyService.ValidateQuery(sql, config);

        Assert.False(result.IsValid);
        Assert.Equal(SqlSafetyRisk.BlockedReadOnly, result.Risk);
    }

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("drop view v_user_summary")]
    [InlineData("DROP DATABASE main_production")]
    [InlineData("TRUNCATE TABLE event_logs")]
    [InlineData("truncate audit_trail")]
    public void ProductionMode_BlocksDropAndTruncate(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "Main Postgres",
            IsProduction = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.False(result.IsValid);
        Assert.Equal(SqlSafetyRisk.BlockedDangerous, result.Risk);
        Assert.NotNull(result.BlockedReason);
        Assert.Contains("Alerta de Produção", result.BlockedReason);
    }

    [Theory]
    [InlineData("DELETE FROM users")]
    [InlineData("delete from audit_events;")]
    [InlineData("DELETE FROM sessions   ")]
    public void ProductionMode_BlocksDeleteWithoutWhere(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "Production DB",
            IsProduction = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.False(result.IsValid);
        Assert.Equal(SqlSafetyRisk.BlockedDangerous, result.Risk);
        Assert.Contains("sem cláusula WHERE", result.BlockedReason);
    }

    [Theory]
    [InlineData("UPDATE users SET active = 0")]
    [InlineData("update accounts set balance = 0;")]
    public void ProductionMode_BlocksUpdateWithoutWhere(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "Production DB",
            IsProduction = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.False(result.IsValid);
        Assert.Equal(SqlSafetyRisk.BlockedDangerous, result.Risk);
        Assert.Contains("sem cláusula WHERE", result.BlockedReason);
    }

    [Theory]
    [InlineData("DELETE FROM users WHERE id = 42")]
    [InlineData("UPDATE users SET active = 1 WHERE email = 'test@example.com'")]
    [InlineData("INSERT INTO logs (event) VALUES ('login')")]
    [InlineData("SELECT * FROM users WHERE active = 1")]
    public void ProductionMode_AllowsTargetedQueriesWithWhere(string sql)
    {
        var config = new ConnectionConfig
        {
            Name = "Production Server",
            IsProduction = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.True(result.IsValid);
        Assert.Equal(SqlSafetyRisk.Safe, result.Risk);
    }

    [Fact]
    public void ProductionMode_IgnoresKeywordsInsideStringLiterals()
    {
        var config = new ConnectionConfig
        {
            Name = "Prod DB",
            IsProduction = true
        };

        var sql = "SELECT 'DROP TABLE users' AS query_sample FROM logs WHERE id = 1";
        var result = _safetyService.ValidateQuery(sql, config);

        Assert.True(result.IsValid);
        Assert.Equal(SqlSafetyRisk.Safe, result.Risk);
    }

    [Theory]
    [InlineData("/* critical */ DROP TABLE users", false)]
    [InlineData("-- dangerous step\nTRUNCATE TABLE users", false)]
    public void ProductionMode_DetectsDangerousCommandsBehindComments(string sql, bool expectedValid)
    {
        var config = new ConnectionConfig
        {
            Name = "Production Cluster",
            IsProduction = true
        };

        var result = _safetyService.ValidateQuery(sql, config);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("Database Prod", "", true)]
    [InlineData("Postgres", "Produção", true)]
    [InlineData("Production Server", "General", true)]
    [InlineData("My Server", "PRODUCTION", true)]
    [InlineData("Localhost Dev", "Development", false)]
    [InlineData("Demo Database", "Test", false)]
    public void ConnectionConfig_AutoDetectsProductionEnvironment(string name, string category, bool expectedIsProd)
    {
        var config = new ConnectionConfig
        {
            Name = name,
            Category = category
        };

        Assert.Equal(expectedIsProd, config.IsProductionEnvironment);
    }

    [Fact]
    public void ConnectionConfig_CloneCopiesSafetyProperties()
    {
        var original = new ConnectionConfig
        {
            Name = "Source DB",
            IsReadOnly = true,
            IsProduction = true,
            SafeExecutionMode = true
        };

        var clone = original.Clone();

        Assert.True(clone.IsReadOnly);
        Assert.True(clone.IsProduction);
        Assert.True(clone.SafeExecutionMode);
    }

    [Fact]
    public void NormalDevConnection_AllowsAllOperations()
    {
        var config = new ConnectionConfig
        {
            Name = "Local SQLite",
            Category = "Dev",
            IsReadOnly = false,
            IsProduction = false,
            SafeExecutionMode = false
        };

        var queries = new[]
        {
            "DROP TABLE old_records",
            "TRUNCATE TABLE test_logs",
            "DELETE FROM users",
            "UPDATE users SET active = 1",
            "INSERT INTO users (name) VALUES ('Tester')"
        };

        foreach (var sql in queries)
        {
            var result = _safetyService.ValidateQuery(sql, config);
            Assert.True(result.IsValid, $"Query should be valid on dev: {sql}");
            Assert.Equal(SqlSafetyRisk.Safe, result.Risk);
        }
    }
}
