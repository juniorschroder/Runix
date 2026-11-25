using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
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

    [Fact]
    public async Task CollectOracleHealthAsync_WithDbaViews_ExtractsTablespaceAndTableMetrics()
    {
        var fakeOra = new FakeOracleDatabaseService();
        fakeOra.QueryHandler = sql =>
        {
            if (sql.Contains("dba_data_files", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "tablespace_name", "total_bytes", "free_bytes", "used_bytes", "file_count", "autoextend_count" },
                    new List<List<object?>>
                    {
                        new() { "USERS", 104857600L, 20971520L, 83886080L, 2L, 1L },
                        new() { "SYSTEM", 524288000L, 209715200L, 314572800L, 1L, 0L }
                    },
                    1.5));
            }
            if (sql.Contains("all_segments", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "owner", "segment_name", "tablespace_name", "data_bytes", "index_bytes", "row_count" },
                    new List<List<object?>>
                    {
                        new() { "TESTUSER", "CUSTOMERS", "USERS", 5242880L, 1048576L, 1000L }
                    },
                    2.0));
            }
            if (sql.Contains("v$sysstat", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "hit_ratio" },
                    new List<List<object?>>
                    {
                        new() { 98.75 }
                    },
                    0.5));
            }
            if (sql.Contains("v$session", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "total_conns", "active_conns", "blocked_sessions" },
                    new List<List<object?>>
                    {
                        new() { 15L, 4L, 0L }
                    },
                    0.5));
            }
            if (sql.Contains("count(*)", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "cnt" },
                    new List<List<object?>> { new() { 5L } },
                    0.5));
            }
            return Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 0.1));
        };

        var healthService = new DatabaseHealthService(fakeOra);
        var report = await healthService.CollectHealthReportAsync(includeIntegrityCheck: false);

        Assert.Equal(DatabaseType.Oracle, report.DatabaseType);
        Assert.Equal(2, report.TablespaceMetrics.Count);
        Assert.Equal("USERS", report.TablespaceMetrics[0].TablespaceName);
        Assert.Equal(104857600L, report.TablespaceMetrics[0].TotalSizeBytes);
        Assert.Equal(20971520L, report.TablespaceMetrics[0].FreeSizeBytes);
        Assert.Equal(83886080L, report.TablespaceMetrics[0].UsedSizeBytes);
        Assert.Equal(80.0, report.TablespaceMetrics[0].UsedPercent);
        Assert.Equal(2, report.TablespaceMetrics[0].FileCount);
        Assert.Equal(1, report.TablespaceMetrics[0].AutoextensibleCount);

        Assert.Single(report.TableMetrics);
        Assert.Equal("CUSTOMERS", report.TableMetrics[0].TableName);
        Assert.Equal(1000L, report.TableMetrics[0].RowCount);

        Assert.Equal(98.75, report.CacheOrPageEfficiencyPercent);
        Assert.Equal(4, report.ActiveConnections);
        Assert.Equal(0, report.BlockedSessions);
        Assert.Equal(HealthSeverity.Healthy, report.OverallSeverity);

        string md = report.ToMarkdownReport();
        Assert.Contains("## Tablespaces Oracle", md);
        Assert.Contains("USERS", md);
        Assert.Contains("CUSTOMERS", md);
    }

    [Fact]
    public async Task CollectOracleHealthAsync_FallbackToUserViews_WhenDbaViewsFail()
    {
        var fakeOra = new FakeOracleDatabaseService();
        fakeOra.QueryHandler = sql =>
        {
            if (sql.Contains("dba_data_files", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateError("ORA-00942: table or view does not exist"));
            }
            if (sql.Contains("user_tablespaces", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "tablespace_name", "total_bytes", "free_bytes", "used_bytes", "file_count", "autoextend_count" },
                    new List<List<object?>>
                    {
                        new() { "APP_DATA", 50000000L, 0L, 50000000L, 1L, 0L }
                    },
                    1.0));
            }
            if (sql.Contains("all_segments", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateError("ORA-00942: table or view does not exist"));
            }
            if (sql.Contains("user_segments", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "owner", "segment_name", "tablespace_name", "data_bytes", "index_bytes", "row_count" },
                    new List<List<object?>>
                    {
                        new() { "APPUSER", "INVOICES", "APP_DATA", 10000000L, 2000000L, 500L }
                    },
                    1.2));
            }
            if (sql.Contains("v$sysstat", StringComparison.OrdinalIgnoreCase) || sql.Contains("v$session", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateError("ORA-00942: table or view does not exist"));
            }
            return Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 0.1));
        };

        var healthService = new DatabaseHealthService(fakeOra);
        var report = await healthService.CollectHealthReportAsync(includeIntegrityCheck: false);

        Assert.Equal(DatabaseType.Oracle, report.DatabaseType);
        Assert.Single(report.TablespaceMetrics);
        Assert.Equal("APP_DATA", report.TablespaceMetrics[0].TablespaceName);
        Assert.Equal(50000000L, report.TablespaceMetrics[0].TotalSizeBytes);
        Assert.Equal(0L, report.TablespaceMetrics[0].FreeSizeBytes);

        Assert.Single(report.TableMetrics);
        Assert.Equal("INVOICES", report.TableMetrics[0].TableName);

        // Fallback hit ratio and connection values
        Assert.Equal(100.0, report.CacheOrPageEfficiencyPercent);
        Assert.Equal(1, report.ActiveConnections);
        Assert.Equal(0, report.BlockedSessions);
    }

    [Fact]
    public async Task CollectOracleHealthAsync_DetectsBlockedSessions_ElevatesSeverityToWarning()
    {
        var fakeOra = new FakeOracleDatabaseService();
        fakeOra.QueryHandler = sql =>
        {
            if (sql.Contains("dba_data_files", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "tablespace_name", "total_bytes", "free_bytes", "used_bytes", "file_count", "autoextend_count" },
                    new List<List<object?>>
                    {
                        new() { "USERS", 100000000L, 50000000L, 50000000L, 1L, 1L }
                    },
                    1.0));
            }
            if (sql.Contains("v$sysstat", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "hit_ratio" },
                    new List<List<object?>> { new() { 99.0 } },
                    0.5));
            }
            if (sql.Contains("v$session", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "total_conns", "active_conns", "blocked_sessions" },
                    new List<List<object?>>
                    {
                        new() { 20L, 5L, 2L }
                    },
                    0.5));
            }
            return Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 0.1));
        };

        var healthService = new DatabaseHealthService(fakeOra);
        var report = await healthService.CollectHealthReportAsync(includeIntegrityCheck: false);

        Assert.Equal(2, report.BlockedSessions);
        Assert.Equal(HealthSeverity.Warning, report.OverallSeverity);
        Assert.Contains("bloqueada(s) por locks", report.StatusSummary);

        string md = report.ToMarkdownReport();
        Assert.Contains("Sessões Bloqueadas (Locks)", md);
        Assert.Contains("2 sessão(ões) em espera", md);
    }

    [Fact]
    public void GetMaintenanceSqlPreview_Oracle_GeneratesCorrectSql()
    {
        var fakeOra = new FakeOracleDatabaseService();
        var healthService = new DatabaseHealthService(fakeOra);

        string vacuumSql = healthService.GetMaintenanceSqlPreview(MaintenanceOperationType.Vacuum, "CUSTOMERS");
        Assert.Equal("ALTER TABLE \"CUSTOMERS\" SHRINK SPACE COMPACT;", vacuumSql);

        string analyzeSql = healthService.GetMaintenanceSqlPreview(MaintenanceOperationType.Analyze, "CUSTOMERS");
        Assert.Equal("ANALYZE TABLE \"CUSTOMERS\" COMPUTE STATISTICS;", analyzeSql);

        string reindexSql = healthService.GetMaintenanceSqlPreview(MaintenanceOperationType.Reindex, "IDX_CUST_EMAIL");
        Assert.Equal("ALTER INDEX \"IDX_CUST_EMAIL\" REBUILD;", reindexSql);

        string integritySql = healthService.GetMaintenanceSqlPreview(MaintenanceOperationType.IntegrityCheck);
        Assert.Contains("all_constraints", integritySql);
        Assert.Contains("all_objects", integritySql);
    }

    [Fact]
    public async Task RunIntegrityChecksAsync_Oracle_DetectsDisabledConstraintsAndInvalidObjects()
    {
        var fakeOra = new FakeOracleDatabaseService();
        fakeOra.QueryHandler = sql =>
        {
            if (sql.Contains("all_constraints", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "owner", "constraint_name", "table_name", "constraint_type" },
                    new List<List<object?>>
                    {
                        new() { "TESTUSER", "FK_ORDERS_USERS", "ORDERS", "R" }
                    },
                    1.0));
            }
            if (sql.Contains("all_objects", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(QueryResult.CreateSuccess(
                    new List<string> { "owner", "object_name", "object_type", "status" },
                    new List<List<object?>>
                    {
                        new() { "TESTUSER", "PRC_CALC_TAX", "PROCEDURE", "INVALID" }
                    },
                    1.0));
            }
            return Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 0.1));
        };

        var healthService = new DatabaseHealthService(fakeOra);
        var result = await healthService.ExecuteMaintenanceAsync(MaintenanceOperationType.IntegrityCheck);

        Assert.True(result.Success);
        Assert.Equal(2, result.IntegrityIssues.Count);

        var constraintIssue = Assert.Single(result.IntegrityIssues, i => i.CheckType == "Disabled Constraint");
        Assert.Equal("ORDERS", constraintIssue.TableName);
        Assert.Contains("FK_ORDERS_USERS", constraintIssue.Description);

        var objectIssue = Assert.Single(result.IntegrityIssues, i => i.CheckType == "Invalid Object");
        Assert.Equal("PRC_CALC_TAX", objectIssue.TableName);
        Assert.Contains("INVALID", objectIssue.Description);
    }

    private sealed class FakeOracleDatabaseService : IDatabaseService
    {
        public ConnectionConfig Config { get; set; } = new ConnectionConfig
        {
            Type = DatabaseType.Oracle,
            Host = "localhost",
            Port = 1521,
            Database = "XEPDB1",
            Username = "TESTUSER"
        };
        public bool IsConnected { get; set; } = true;
        public CircuitState CurrentCircuitState => CircuitState.Closed;
        public event Action<ResilienceEvent>? ResilienceEventOccurred;

        public Func<string, Task<QueryResult>>? QueryHandler { get; set; }

        public Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000, System.Threading.CancellationToken cancellationToken = default)
        {
            if (QueryHandler != null)
            {
                return QueryHandler(sql);
            }
            return Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 0.1));
        }

        public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000, System.Threading.CancellationToken cancellationToken = default)
        {
            var res = await ExecuteQueryAsync(sql, limit, cancellationToken);
            return new List<QueryResult> { res };
        }

        public Task ConnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> TestConnectionAsync() => Task.FromResult(true);
        public Task<ConnectionDiagnosticsResult> TestConnectionDiagnosticsAsync() => Task.FromResult(new ConnectionDiagnosticsResult(true, null, 5, "Oracle 23ai", "None"));
        public Task<List<TableInfo>> GetTablesAsync() => Task.FromResult(new List<TableInfo>());
        public Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName) => Task.FromResult(new List<ColumnInfo>());
        public Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName) => Task.FromResult(new List<TableIndexInfo>());
        public Task<string> GetTableDdlAsync(string tableName) => Task.FromResult("");
        public Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null) => Task.FromResult(new List<ForeignKeyInfo>());
        public Task<ErdGraphModel> GetErdGraphAsync() => Task.FromResult(new ErdGraphModel());
        public Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true)
            => Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 1.0));
        public Task<QueryResult> ExecuteBatchTransactionalAsync(IReadOnlyList<ParameterizedCommand> commands, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(new QueryResult { Success = true });
        public void Dispose() { }
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
