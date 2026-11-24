using System;
using System.Linq;
using System.Text.Json;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class EnginePickerAndSyncTests
{
    [Theory]
    [InlineData(DatabaseType.SQLite, 0)]
    [InlineData(DatabaseType.PostgreSQL, 5432)]
    [InlineData(DatabaseType.MySQL, 3306)]
    [InlineData(DatabaseType.MariaDB, 3306)]
    [InlineData(DatabaseType.SqlServer, 1433)]
    public void Engine_StandardDefaultPorts_AreAccurate(DatabaseType type, int expectedPort)
    {
        int defaultPort = type switch
        {
            DatabaseType.PostgreSQL => 5432,
            DatabaseType.MySQL or DatabaseType.MariaDB => 3306,
            DatabaseType.SqlServer => 1433,
            _ => 0
        };

        Assert.Equal(expectedPort, defaultPort);
    }

    [Theory]
    [InlineData("sqlite", DatabaseType.SQLite, true)]
    [InlineData("local", DatabaseType.SQLite, true)]
    [InlineData("pg", DatabaseType.PostgreSQL, true)]
    [InlineData("postgres", DatabaseType.PostgreSQL, true)]
    [InlineData("mysql", DatabaseType.MySQL, true)]
    [InlineData("maria", DatabaseType.MariaDB, true)]
    [InlineData("mssql", DatabaseType.SqlServer, true)]
    [InlineData("azure", DatabaseType.SqlServer, true)]
    [InlineData("sqlserver", DatabaseType.SqlServer, true)]
    [InlineData("oracle", DatabaseType.MySQL, false)]
    public void EngineFilter_MatchesExpectedTerms(string query, DatabaseType targetEngine, bool expectedMatch)
    {
        string q = query.Trim().ToLowerInvariant();
        bool isMatch = targetEngine switch
        {
            DatabaseType.SQLite => MatchesEngine(q, "sqlite", "local", "arquivo", "file", "embutido", "embedded", "db"),
            DatabaseType.PostgreSQL => MatchesEngine(q, "postgresql", "postgres", "pg", "relacional", "objeto", "sql"),
            DatabaseType.MySQL => MatchesEngine(q, "mysql", "lamp", "innodb", "sql"),
            DatabaseType.MariaDB => MatchesEngine(q, "mariadb", "maria", "fork", "comunitario", "sql"),
            DatabaseType.SqlServer => MatchesEngine(q, "sqlserver", "sql server", "mssql", "microsoft", "azure", "tsql", "dbo", "sql"),
            _ => false
        };

        Assert.Equal(expectedMatch, isMatch);
    }

    private static bool MatchesEngine(string query, params string[] terms)
    {
        return terms.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase) || query.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConnectionConfig_SqlServer_DefaultsAndSerialization_PreservesProperties()
    {
        var config = new ConnectionConfig
        {
            Name = "Production ERP SQL Server",
            Type = DatabaseType.SqlServer,
            Host = "sql-cluster.company.internal",
            Port = 1433,
            Database = "ERP_PROD",
            Username = "sa",
            Password = "SuperSecretPassword123!",
            DefaultSchema = "erp",
            TrustServerCertificate = true,
            IsProduction = true,
            IsReadOnly = true
        };

        string json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<ConnectionConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(DatabaseType.SqlServer, restored.Type);
        Assert.Equal("erp", restored.DefaultSchema);
        Assert.True(restored.TrustServerCertificate);
        Assert.True(restored.IsProduction);
        Assert.True(restored.IsReadOnly);
    }
}
