using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Database;

public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly IDatabaseService _databaseService;

    public DatabaseHealthService(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public async Task<DatabaseHealthReport> CollectHealthReportAsync(
        bool includeIntegrityCheck = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _databaseService.Config.Type == DatabaseType.PostgreSQL
            ? await CollectPostgresHealthAsync(cancellationToken)
            : await CollectSqliteHealthAsync(includeIntegrityCheck, cancellationToken);
    }

    public string GetMaintenanceSqlPreview(MaintenanceOperationType operation, string? tableName = null)
    {
        bool isPg = _databaseService.Config.Type == DatabaseType.PostgreSQL;
        string? quotedTable = string.IsNullOrWhiteSpace(tableName) ? null : QuoteIdentifier(tableName);

        return operation switch
        {
            MaintenanceOperationType.Vacuum => isPg && quotedTable != null ? $"VACUUM {quotedTable};" : "VACUUM;",
            MaintenanceOperationType.Analyze => quotedTable != null ? $"ANALYZE {quotedTable};" : "ANALYZE;",
            MaintenanceOperationType.VacuumAnalyze => isPg
                ? (quotedTable != null ? $"VACUUM (ANALYZE) {quotedTable};" : "VACUUM (ANALYZE);")
                : "VACUUM;\nANALYZE;",
            MaintenanceOperationType.Reindex => isPg
                ? (quotedTable != null ? $"REINDEX TABLE {quotedTable};" : "REINDEX DATABASE CURRENT_DATABASE();")
                : (quotedTable != null ? $"REINDEX {quotedTable};" : "REINDEX;"),
            MaintenanceOperationType.IntegrityCheck => isPg
                ? "SELECT relname, n_dead_tup, last_vacuum, last_analyze FROM pg_stat_user_tables;"
                : "PRAGMA integrity_check(50);\nPRAGMA foreign_key_check;",
            _ => "ANALYZE;"
        };
    }

    public async Task<MaintenanceExecutionResult> ExecuteMaintenanceAsync(
        MaintenanceOperationType operation,
        string? tableName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        long sizeBefore = await GetCurrentTotalSizeBytesAsync();
        string sqlPreview = GetMaintenanceSqlPreview(operation, tableName);

        if (operation == MaintenanceOperationType.IntegrityCheck)
        {
            var issues = await RunIntegrityChecksAsync();
            sw.Stop();
            return new MaintenanceExecutionResult
            {
                Success = true,
                Operation = operation,
                TargetTable = tableName,
                ExecutedSql = sqlPreview,
                SizeBeforeBytes = sizeBefore,
                SizeAfterBytes = sizeBefore,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                Message = issues.Count == 0
                    ? "✓ Verificação concluída: 0 inconsistências estruturais ou de Foreign Keys."
                    : $"⚠ Encontradas {issues.Count} inconsistência(s) na verificação de integridade.",
                IntegrityIssues = issues
            };
        }

        // Execute statements sequentially outside explicit transaction blocks (required for VACUUM)
        string[] statements = sqlPreview
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string stmt in statements)
        {
            var res = await _databaseService.ExecuteQueryAsync(stmt);
            if (!res.Success)
            {
                sw.Stop();
                return new MaintenanceExecutionResult
                {
                    Success = false,
                    Operation = operation,
                    TargetTable = tableName,
                    ExecutedSql = sqlPreview,
                    SizeBeforeBytes = sizeBefore,
                    SizeAfterBytes = sizeBefore,
                    ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                    Message = $"✗ Falha ao executar {operation}: {res.ErrorMessage}"
                };
            }
        }

        long sizeAfter = await GetCurrentTotalSizeBytesAsync();
        sw.Stop();
        long reclaimed = Math.Max(0, sizeBefore - sizeAfter);

        return new MaintenanceExecutionResult
        {
            Success = true,
            Operation = operation,
            TargetTable = tableName,
            ExecutedSql = sqlPreview,
            SizeBeforeBytes = sizeBefore,
            SizeAfterBytes = sizeAfter,
            ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
            Message = reclaimed > 0
                ? $"✓ {operation} concluído em {sw.Elapsed.TotalMilliseconds:F0} ms! Espaço recuperado: {DatabaseHealthReport.FormatBytes(reclaimed)}."
                : $"✓ {operation} concluído com sucesso em {sw.Elapsed.TotalMilliseconds:F0} ms."
        };
    }

    private async Task<DatabaseHealthReport> CollectSqliteHealthAsync(
        bool includeIntegrityCheck,
        CancellationToken cancellationToken)
    {
        long pageCount = await ExecuteScalarLongAsync("PRAGMA page_count;", 1);
        int pageSize = (int)await ExecuteScalarLongAsync("PRAGMA page_size;", 4096);
        long freelistCount = await ExecuteScalarLongAsync("PRAGMA freelist_count;", 0);
        string journalMode = (await ExecuteScalarStringAsync("PRAGMA journal_mode;", "delete")).ToUpperInvariant();

        long logicalSizeBytes = pageCount * pageSize;
        long fileSizeBytes = logicalSizeBytes;
        string targetName = "SQLite In-Memory";

        if (!string.IsNullOrWhiteSpace(_databaseService.Config.FilePath))
        {
            targetName = Path.GetFileName(_databaseService.Config.FilePath);
            if (File.Exists(_databaseService.Config.FilePath))
            {
                try
                {
                    fileSizeBytes = new FileInfo(_databaseService.Config.FilePath).Length;
                }
                catch
                {
                    fileSizeBytes = logicalSizeBytes;
                }
            }
        }

        long freeBytes = freelistCount * pageSize;
        double fragPercent = pageCount > 0 ? Math.Round((double)freelistCount * 100.0 / pageCount, 1) : 0.0;
        double efficiencyPercent = Math.Clamp(100.0 - fragPercent, 0.0, 100.0);

        var allObjects = await _databaseService.GetTablesAsync();
        var tables = allObjects.Where(t => !t.IsView).ToList();
        int viewCount = allObjects.Count(t => t.IsView);

        // Try reading exact sizes from dbstat if available
        var dbstatSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var dbstatRes = await _databaseService.ExecuteQueryAsync("SELECT name, SUM(pgsize) FROM dbstat GROUP BY name;");
        if (dbstatRes.Success)
        {
            foreach (var row in dbstatRes.Rows)
            {
                if (row.Count >= 2 && row[0] != null)
                {
                    dbstatSizes[row[0]!.ToString()!] = ParseLong(row[1]);
                }
            }
        }

        var tableMetrics = new List<TableStorageMetric>();
        var indexFindings = new List<IndexHealthFinding>();
        int totalIndexCount = 0;

        foreach (var tbl in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long rowCount = tbl.RowCount ?? await ExecuteScalarLongAsync($"SELECT COUNT(*) FROM {QuoteIdentifier(tbl.Name)};", 0);

            var idxListRes = await _databaseService.ExecuteQueryAsync($"PRAGMA index_list({QuoteIdentifier(tbl.Name)});");
            int tableIdxCount = idxListRes.Success ? idxListRes.Rows.Count : 0;
            totalIndexCount += tableIdxCount;

            // Check index column signatures for duplicate detection
            var indexSignatures = new List<(string IndexName, string ColumnsKey)>();
            long idxBytes = 0;

            if (idxListRes.Success)
            {
                foreach (var idxRow in idxListRes.Rows)
                {
                    if (idxRow.Count < 2 || idxRow[1] == null) continue;
                    string idxName = idxRow[1]!.ToString()!;
                    if (dbstatSizes.TryGetValue(idxName, out long exactIdxSize))
                    {
                        idxBytes += exactIdxSize;
                    }

                    var infoRes = await _databaseService.ExecuteQueryAsync($"PRAGMA index_info({QuoteIdentifier(idxName)});");
                    if (infoRes.Success && infoRes.Rows.Count > 0)
                    {
                        var cols = infoRes.Rows
                            .Where(r => r.Count >= 3 && r[2] != null)
                            .Select(r => r[2]!.ToString()!)
                            .ToList();
                        if (cols.Count > 0)
                        {
                            indexSignatures.Add((idxName, string.Join(", ", cols)));
                        }
                    }
                }
            }

            indexSignatures = indexSignatures
                .OrderBy(x => x.IndexName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Detect duplicate/redundant indexes on the same table
            for (int i = 0; i < indexSignatures.Count; i++)
            {
                for (int j = i + 1; j < indexSignatures.Count; j++)
                {
                    if (string.Equals(indexSignatures[i].ColumnsKey, indexSignatures[j].ColumnsKey, StringComparison.OrdinalIgnoreCase))
                    {
                        indexFindings.Add(new IndexHealthFinding
                        {
                            Schema = tbl.Schema,
                            TableName = tbl.Name,
                            IndexName = indexSignatures[j].IndexName,
                            FindingType = "Índice Duplicado",
                            ColumnsSummary = indexSignatures[j].ColumnsKey,
                            Description = $"Cobre exatamente as mesmas colunas ({indexSignatures[j].ColumnsKey}) que '{indexSignatures[i].IndexName}'.",
                            RecommendationSql = $"DROP INDEX IF EXISTS {QuoteIdentifier(indexSignatures[j].IndexName)};",
                            Severity = HealthSeverity.Warning
                        });
                    }
                }
            }

            // Estimate or use dbstat for table data size
            int colCount = 4;
            try
            {
                var cols = await _databaseService.GetTableColumnsAsync(tbl.Name);
                if (cols.Count > 0) colCount = cols.Count;
            }
            catch
            {
                // Ignore column lookup error
            }
            long dataBytes = dbstatSizes.TryGetValue(tbl.Name, out long exactTblSize)
                ? exactTblSize
                : Math.Max(pageSize, rowCount * colCount * 36L);

            if (idxBytes == 0 && tableIdxCount > 0)
            {
                idxBytes = Math.Max(pageSize / 2, rowCount * tableIdxCount * 18L);
            }

            tableMetrics.Add(new TableStorageMetric
            {
                Schema = tbl.Schema,
                TableName = tbl.Name,
                RowCount = rowCount,
                DataSizeBytes = dataBytes,
                IndexSizeBytes = idxBytes,
                IndexCount = tableIdxCount
            });
        }

        tableMetrics = tableMetrics.OrderByDescending(m => m.TotalSizeBytes).ToList();

        var integrityIssues = includeIntegrityCheck
            ? await RunIntegrityChecksAsync()
            : new List<IntegrityCheckIssue>();

        long sumData = tableMetrics.Sum(t => t.DataSizeBytes);
        long sumIdx = tableMetrics.Sum(t => t.IndexSizeBytes);
        long totalSize = Math.Max(fileSizeBytes, sumData + sumIdx + freeBytes);

        var severity = HealthSeverity.Healthy;
        string summary = $"Saúde excelente ({journalMode}, {fragPercent:F1}% páginas livres).";

        if (integrityIssues.Count > 0)
        {
            severity = HealthSeverity.Critical;
            summary = $"Atenção crítica: {integrityIssues.Count} erro(s) de integridade detectado(s).";
        }
        else if (fragPercent >= 20.0 || indexFindings.Count > 0)
        {
            severity = HealthSeverity.Warning;
            summary = fragPercent >= 20.0
                ? $"Fragmentação de páginas em {fragPercent:F1}% — recomendado executar VACUUM."
                : $"{indexFindings.Count} alerta(s) de otimização de índices encontrado(s).";
        }

        return new DatabaseHealthReport
        {
            DatabaseType = DatabaseType.SQLite,
            TargetName = targetName,
            CollectedAtUtc = DateTime.UtcNow,
            TotalSizeBytes = totalSize,
            DataSizeBytes = sumData,
            IndexSizeBytes = sumIdx,
            FreeOrReclaimableBytes = freeBytes,
            CacheOrPageEfficiencyPercent = efficiencyPercent,
            FragmentationOrDeadTuplePercent = fragPercent,
            TableCount = tables.Count,
            ViewCount = viewCount,
            IndexCount = totalIndexCount,
            ActiveConnections = 1,
            MaxConnections = 1,
            JournalOrWalMode = journalMode,
            PageSize = pageSize,
            PageCount = pageCount,
            FreelistCount = freelistCount,
            OverallSeverity = severity,
            StatusSummary = summary,
            TableMetrics = tableMetrics,
            IndexFindings = indexFindings,
            IntegrityIssues = integrityIssues
        };
    }

    private async Task<DatabaseHealthReport> CollectPostgresHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long dbSize = await ExecuteScalarLongAsync("SELECT pg_database_size(current_database());", 0);
        int activeConns = (int)await ExecuteScalarLongAsync("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database();", 1);
        int maxConns = (int)await ExecuteScalarLongAsync("SELECT setting::int FROM pg_settings WHERE name = 'max_connections';", 100);
        double cacheHitRatio = await ExecuteScalarDoubleAsync(@"
            SELECT COALESCE(
                ROUND(100.0 * SUM(heap_blks_hit + idx_blks_hit) / NULLIF(SUM(heap_blks_hit + heap_blks_read + idx_blks_hit + idx_blks_read), 0), 2),
                100.0
            )
            FROM pg_statio_user_tables;", 99.5);

        var tblStatsRes = await _databaseService.ExecuteQueryAsync(@"
            SELECT
                schemaname,
                relname,
                COALESCE(n_live_tup, 0) AS row_cnt,
                pg_relation_size(relid) AS data_bytes,
                pg_indexes_size(relid) AS idx_bytes,
                COALESCE(n_dead_tup, 0) AS dead_tup,
                COALESCE(seq_scan, 0) AS seq_scan,
                COALESCE(idx_scan, 0) AS idx_scan
            FROM pg_stat_user_tables
            ORDER BY pg_total_relation_size(relid) DESC;");

        var tableMetrics = new List<TableStorageMetric>();
        long totalLive = 0;
        long totalDead = 0;

        if (tblStatsRes.Success)
        {
            foreach (var row in tblStatsRes.Rows)
            {
                if (row.Count < 8) continue;
                long live = ParseLong(row[2]);
                long dead = ParseLong(row[5]);
                totalLive += live;
                totalDead += dead;

                tableMetrics.Add(new TableStorageMetric
                {
                    Schema = row[0]?.ToString() ?? "public",
                    TableName = row[1]?.ToString() ?? string.Empty,
                    RowCount = live,
                    DataSizeBytes = ParseLong(row[3]),
                    IndexSizeBytes = ParseLong(row[4]),
                    DeadTuples = dead,
                    SeqScans = ParseLong(row[6]),
                    IdxScans = ParseLong(row[7])
                });
            }
        }

        // Detect unused non-unique indexes in PostgreSQL
        var indexFindings = new List<IndexHealthFinding>();
        var unusedIdxRes = await _databaseService.ExecuteQueryAsync(@"
            SELECT
                s.schemaname,
                s.relname AS tablename,
                s.indexrelname AS indexname,
                pg_relation_size(s.indexrelid) AS idx_size
            FROM pg_stat_user_indexes s
            JOIN pg_index i ON s.indexrelid = i.indexrelid
            WHERE s.idx_scan = 0 AND NOT i.indisunique AND NOT i.indisprimary;");

        if (unusedIdxRes.Success)
        {
            foreach (var row in unusedIdxRes.Rows)
            {
                if (row.Count < 4) continue;
                string schema = row[0]?.ToString() ?? "public";
                string tbl = row[1]?.ToString() ?? string.Empty;
                string idx = row[2]?.ToString() ?? string.Empty;
                long sz = ParseLong(row[3]);

                indexFindings.Add(new IndexHealthFinding
                {
                    Schema = schema,
                    TableName = tbl,
                    IndexName = idx,
                    FindingType = "Índice Não Utilizado (0 scans)",
                    ColumnsSummary = DatabaseHealthReport.FormatBytes(sz),
                    Description = $"Índice secundário com 0 leituras registradas ({DatabaseHealthReport.FormatBytes(sz)} ocupados).",
                    RecommendationSql = $"DROP INDEX IF EXISTS {QuoteIdentifier(schema)}.{QuoteIdentifier(idx)};",
                    Severity = HealthSeverity.Warning
                });
            }
        }

        int viewCount = (int)await ExecuteScalarLongAsync(
            "SELECT count(*) FROM information_schema.views WHERE table_schema NOT IN ('pg_catalog', 'information_schema');", 0);
        int indexCount = (int)await ExecuteScalarLongAsync(
            "SELECT count(*) FROM pg_indexes WHERE schemaname NOT IN ('pg_catalog', 'information_schema');", 0);

        long dataBytesTotal = tableMetrics.Sum(t => t.DataSizeBytes);
        long idxBytesTotal = tableMetrics.Sum(t => t.IndexSizeBytes);
        double deadPercent = (totalLive + totalDead) > 0
            ? Math.Round((double)totalDead * 100.0 / (totalLive + totalDead), 1)
            : 0.0;
        long estReclaimable = (long)Math.Round(dataBytesTotal * (deadPercent / 100.0));

        var severity = deadPercent >= 25.0 || cacheHitRatio < 85.0
            ? HealthSeverity.Warning
            : HealthSeverity.Healthy;

        string summary = severity == HealthSeverity.Healthy
            ? $"PostgreSQL saudável (Cache Hit: {cacheHitRatio:F1}%, Dead Tuples: {deadPercent:F1}%)."
            : $"Recomendado manutenção: Dead Tuples em {deadPercent:F1}%, Cache Hit em {cacheHitRatio:F1}%.";

        return new DatabaseHealthReport
        {
            DatabaseType = DatabaseType.PostgreSQL,
            TargetName = $"{_databaseService.Config.Database}@{_databaseService.Config.Host}",
            CollectedAtUtc = DateTime.UtcNow,
            TotalSizeBytes = Math.Max(dbSize, dataBytesTotal + idxBytesTotal),
            DataSizeBytes = dataBytesTotal,
            IndexSizeBytes = idxBytesTotal,
            FreeOrReclaimableBytes = estReclaimable,
            CacheOrPageEfficiencyPercent = cacheHitRatio,
            FragmentationOrDeadTuplePercent = deadPercent,
            TableCount = tableMetrics.Count,
            ViewCount = viewCount,
            IndexCount = indexCount,
            ActiveConnections = activeConns,
            MaxConnections = maxConns,
            JournalOrWalMode = "WAL",
            OverallSeverity = severity,
            StatusSummary = summary,
            TableMetrics = tableMetrics,
            IndexFindings = indexFindings
        };
    }

    private async Task<List<IntegrityCheckIssue>> RunIntegrityChecksAsync()
    {
        var issues = new List<IntegrityCheckIssue>();
        if (_databaseService.Config.Type == DatabaseType.SQLite)
        {
            var icRes = await _databaseService.ExecuteQueryAsync("PRAGMA integrity_check(50);");
            if (icRes.Success)
            {
                foreach (var row in icRes.Rows)
                {
                    string msg = row.Count > 0 ? row[0]?.ToString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrWhiteSpace(msg) && !string.Equals(msg, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new IntegrityCheckIssue
                        {
                            CheckType = "PRAGMA integrity_check",
                            Description = msg
                        });
                    }
                }
            }

            var fkRes = await _databaseService.ExecuteQueryAsync("PRAGMA foreign_key_check;");
            if (fkRes.Success)
            {
                foreach (var row in fkRes.Rows)
                {
                    if (row.Count >= 3)
                    {
                        string tbl = row[0]?.ToString() ?? string.Empty;
                        string rowId = row[1]?.ToString() ?? string.Empty;
                        string parent = row[2]?.ToString() ?? string.Empty;
                        issues.Add(new IntegrityCheckIssue
                        {
                            CheckType = "PRAGMA foreign_key_check",
                            TableName = tbl,
                            RowId = rowId,
                            ParentTable = parent,
                            Description = $"Violação de FK na tabela '{tbl}' (rowid={rowId}) referenciando '{parent}'."
                        });
                    }
                }
            }
        }
        return issues;
    }

    private async Task<long> GetCurrentTotalSizeBytesAsync()
    {
        if (_databaseService.Config.Type == DatabaseType.PostgreSQL)
        {
            return await ExecuteScalarLongAsync("SELECT pg_database_size(current_database());", 0);
        }

        if (!string.IsNullOrWhiteSpace(_databaseService.Config.FilePath) && File.Exists(_databaseService.Config.FilePath))
        {
            try
            {
                return new FileInfo(_databaseService.Config.FilePath).Length;
            }
            catch
            {
                // Fall back to page calculation
            }
        }

        long pageCount = await ExecuteScalarLongAsync("PRAGMA page_count;", 1);
        long pageSize = await ExecuteScalarLongAsync("PRAGMA page_size;", 4096);
        return pageCount * pageSize;
    }

    private async Task<long> ExecuteScalarLongAsync(string sql, long fallback)
    {
        var res = await _databaseService.ExecuteQueryAsync(sql);
        if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0)
        {
            return ParseLong(res.Rows[0][0], fallback);
        }
        return fallback;
    }

    private async Task<double> ExecuteScalarDoubleAsync(string sql, double fallback)
    {
        var res = await _databaseService.ExecuteQueryAsync(sql);
        if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
        {
            if (double.TryParse(res.Rows[0][0]!.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }
        }
        return fallback;
    }

    private async Task<string> ExecuteScalarStringAsync(string sql, string fallback)
    {
        var res = await _databaseService.ExecuteQueryAsync(sql);
        if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
        {
            return res.Rows[0][0]!.ToString() ?? fallback;
        }
        return fallback;
    }

    private static long ParseLong(object? val, long fallback = 0)
    {
        if (val is null || val is DBNull) return fallback;
        if (val is long l) return l;
        if (val is int i) return i;
        if (long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }
        return fallback;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{(identifier ?? string.Empty).Replace("\"", "\"\"")}\"";
    }
}
