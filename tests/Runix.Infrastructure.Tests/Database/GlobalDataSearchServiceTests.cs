using System;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;
using Xunit;

namespace Runix.Infrastructure.Tests.Database;

public class GlobalDataSearchServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _db;

    public GlobalDataSearchServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_gsearch_{Guid.NewGuid():N}.db");
        _db = new SqliteDatabaseService(new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        });
    }

    [Fact]
    public async Task SearchAcrossDatabaseAsync_FindsMatchesAcrossMultipleTables()
    {
        await _db.ConnectAsync();
        await _db.ExecuteQueryAsync("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, email TEXT);");
        await _db.ExecuteQueryAsync("CREATE TABLE audit_logs (id INTEGER PRIMARY KEY, details TEXT);");

        await _db.ExecuteQueryAsync("INSERT INTO customers (name, email) VALUES ('Carlos Silva', 'carlos.unique@runix.dev'), ('Marina', 'marina@test.com');");
        await _db.ExecuteQueryAsync("INSERT INTO audit_logs (details) VALUES ('Login by carlos.unique@runix.dev succeeded');");

        var searchService = new GlobalDataSearchService(_db);
        var report = await searchService.SearchAcrossDatabaseAsync(new GlobalSearchOptions
        {
            SearchTerm = "carlos.unique",
            CaseSensitive = false,
            MaxRowsPerTable = 10
        });

        Assert.Equal(2, report.TablesScanned);
        Assert.Equal(2, report.TablesWithMatches);
        Assert.Equal(2, report.TotalMatchedRows);
        Assert.Contains(report.Matches, m => m.TableName == "customers" && m.MatchedColumns.Contains("email"));
        Assert.Contains(report.Matches, m => m.TableName == "audit_logs" && m.MatchedColumns.Contains("details"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }
}
