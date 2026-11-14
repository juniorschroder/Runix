using System;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;
using Xunit;

namespace Runix.Infrastructure.Tests.Database;

public class DatabaseHealthServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _db;

    public DatabaseHealthServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_health_{Guid.NewGuid():N}.db");
        _db = new SqliteDatabaseService(new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        });
    }

    [Fact]
    public async Task CollectHealthReportAsync_CollectsStorageMetricsAndDetectsDuplicateIndexes()
    {
        await _db.ConnectAsync();
        await _db.ExecuteQueryAsync("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_email TEXT, total REAL);");
        await _db.ExecuteQueryAsync("CREATE INDEX idx_orders_email_1 ON orders(customer_email);");
        await _db.ExecuteQueryAsync("CREATE INDEX idx_orders_email_2 ON orders(customer_email);");
        await _db.ExecuteQueryAsync("INSERT INTO orders (customer_email, total) VALUES ('a@b.com', 100.0), ('c@d.com', 250.0);");

        var healthService = new DatabaseHealthService(_db);
        var report = await healthService.CollectHealthReportAsync(includeIntegrityCheck: true);

        Assert.Equal(DatabaseType.SQLite, report.DatabaseType);
        Assert.Equal(1, report.TableCount);
        Assert.Equal(2, report.IndexCount);
        Assert.True(report.TotalSizeBytes > 0);
        Assert.Single(report.TableMetrics);
        Assert.Equal("orders", report.TableMetrics[0].TableName);
        Assert.Equal(2, report.TableMetrics[0].RowCount);

        // Should detect duplicate index covering (customer_email)
        Assert.Single(report.IndexFindings);
        Assert.Equal("idx_orders_email_2", report.IndexFindings[0].IndexName);
        Assert.Empty(report.IntegrityIssues);

        string md = report.ToMarkdownReport();
        Assert.Contains("orders", md);
        Assert.Contains("idx_orders_email_2", md);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_RunsVacuumAnalyzeReindexAndIntegrityCheck()
    {
        await _db.ConnectAsync();
        await _db.ExecuteQueryAsync("CREATE TABLE items (id INTEGER PRIMARY KEY, payload TEXT);");
        for (int i = 0; i < 50; i++)
        {
            await _db.ExecuteQueryAsync($"INSERT INTO items (payload) VALUES ('{new string('X', 200)}');");
        }
        await _db.ExecuteQueryAsync("DELETE FROM items WHERE id > 10;");

        var healthService = new DatabaseHealthService(_db);

        var vacuumRes = await healthService.ExecuteMaintenanceAsync(MaintenanceOperationType.Vacuum);
        Assert.True(vacuumRes.Success);

        var analyzeRes = await healthService.ExecuteMaintenanceAsync(MaintenanceOperationType.Analyze, "items");
        Assert.True(analyzeRes.Success);

        var reindexRes = await healthService.ExecuteMaintenanceAsync(MaintenanceOperationType.Reindex);
        Assert.True(reindexRes.Success);

        var integrityRes = await healthService.ExecuteMaintenanceAsync(MaintenanceOperationType.IntegrityCheck);
        Assert.True(integrityRes.Success);
        Assert.Empty(integrityRes.IntegrityIssues);
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
