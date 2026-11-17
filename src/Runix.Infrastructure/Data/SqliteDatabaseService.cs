using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Core.Services;

namespace Runix.Infrastructure.Data;

public class SqliteDatabaseService : IDatabaseService
{
    private SqliteConnection? _connection;
    public ConnectionConfig Config { get; }
    public bool IsConnected => _connection != null && _connection.State == System.Data.ConnectionState.Open;
    public CircuitState CurrentCircuitState => CircuitState.Closed;

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public SqliteDatabaseService(ConnectionConfig config)
    {
        Config = config;
    }

    private string BuildConnectionString()
    {
        var path = Config.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("SQLite database file path is required.");
        }

        // Support relative path or demo.db
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        return builder.ConnectionString;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqliteConnection(BuildConnectionString());
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ConnectAsync()
    {
        if (_connection != null)
        {
            await DisconnectAsync();
        }

        _connection = new SqliteConnection(BuildConnectionString());
        await _connection.OpenAsync();
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async Task<List<TableInfo>> GetTablesAsync()
    {
        var tables = new List<TableInfo>();
        if (_connection == null) return tables;

        const string sql = @"
            SELECT name, type 
            FROM sqlite_master 
            WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' 
            ORDER BY type, name;";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            tables.Add(new TableInfo
            {
                Name = name,
                Schema = "main",
                Type = type
            });
        }

        // Try getting row count estimates
        foreach (var tbl in tables)
        {
            if (tbl.Type == "table")
            {
                try
                {
                    using var countCmd = _connection.CreateCommand();
                    countCmd.CommandText = $"SELECT COUNT(*) FROM \"{tbl.Name.Replace("\"", "\"\"")}\";";
                    var count = await countCmd.ExecuteScalarAsync();
                    if (count != null && count != DBNull.Value)
                    {
                        tbl.RowCount = Convert.ToInt64(count);
                    }
                }
                catch
                {
                    // Ignore row count failure
                }
            }
        }

        return tables;
    }

    public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000)
    {
        var resultSets = new List<QueryResult>();
        if (_connection == null)
        {
            return new List<QueryResult>
            {
                new() { Success = false, ErrorMessage = "Not connected to database." }
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();

            int setIndex = 1;
            do
            {
                var result = new QueryResult();
                result.Title = $"Resultado {setIndex}";

                if (reader.HasRows || reader.FieldCount > 0)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        result.Columns.Add(reader.GetName(i));
                    }

                    while (await reader.ReadAsync())
                    {
                        if (result.Rows.Count >= limit)
                            break;

                        var row = new List<object?>(reader.FieldCount);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var val = reader.GetValue(i);
                            row.Add((val == DBNull.Value) ? "NULL" : val);
                        }
                        result.Rows.Add(row);
                    }
                }
                else
                {
                    result.AffectedRows = reader.RecordsAffected;
                }

                result.Success = true;
                resultSets.Add(result);
                setIndex++;
            } while (await reader.NextResultAsync());
        }
        catch (Exception ex)
        {
            if (resultSets.Count == 0)
            {
                resultSets.Add(new QueryResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
            else
            {
                resultSets.Add(new QueryResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Title = "Erro"
                });
            }
        }
        finally
        {
            sw.Stop();
            foreach (var set in resultSets)
            {
                if (set.ElapsedMilliseconds == 0)
                    set.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;
            }
        }

        // If multiple SELECT result sets with columns were returned, prioritize them
        var selectSets = resultSets.Where(r => r.Columns.Count > 0).ToList();
        if (selectSets.Count > 1)
        {
            for (int i = 0; i < selectSets.Count; i++)
            {
                selectSets[i].Title = $"Resultado {i + 1}";
            }
            return selectSets;
        }

        return resultSets.Count > 0 ? resultSets : new List<QueryResult> { new() { Success = true } };
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000)
    {
        var list = await ExecuteMultipleQueriesAsync(sql, limit);
        if (list.Count > 0)
        {
            var primary = list[0];
            primary.SubResults = list;
            return primary;
        }

        return new QueryResult { Success = true };
    }

    public async Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true)
    {
        var safeTable = $"\"{tableName.Replace("\"", "\"\"")}\"";
        var sql = $"SELECT * FROM {safeTable}";

        if (!string.IsNullOrWhiteSpace(filter))
        {
            sql += $" WHERE {filter}";
        }

        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            if (sortColumn.Contains(',') || sortColumn.Contains("ASC", StringComparison.OrdinalIgnoreCase) || sortColumn.Contains("DESC", StringComparison.OrdinalIgnoreCase))
            {
                sql += $" ORDER BY {sortColumn}";
            }
            else
            {
                var safeCol = $"\"{sortColumn.Replace("\"", "\"\"")}\"";
                sql += $" ORDER BY {safeCol} {(sortAsc ? "ASC" : "DESC")}";
            }
        }

        sql += $" LIMIT {limit} OFFSET {offset};";

        return await ExecuteQueryAsync(sql, limit);
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        var columns = new List<ColumnInfo>();
        if (_connection == null) return columns;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // cid, name, type, notnull, dflt_value, pk
            var cid = reader.GetInt32(0);
            var colName = reader.GetString(1);
            var type = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            var dfltVal = reader.IsDBNull(4) ? null : reader.GetString(4);
            var isPk = reader.GetInt32(5) > 0;

            columns.Add(new ColumnInfo
            {
                OrdinalPosition = cid,
                Name = colName,
                DataType = type,
                IsNullable = !notNull,
                IsPrimaryKey = isPk,
                DefaultValue = dfltVal
            });
        }

        // Correlate with foreign keys
        try
        {
            var fks = await GetForeignKeysAsync(tableName);
            foreach (var col in columns)
            {
                var fk = fks.FirstOrDefault(f => string.Equals(f.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase));
                if (fk != null)
                {
                    col.IsForeignKey = true;
                    col.ForeignKeyReference = $"{fk.TargetTable}({fk.TargetColumn})";
                }
            }
        }
        catch
        {
            // Ignore FK correlation errors
        }

        return columns;
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        var indexes = new List<TableIndexInfo>();
        if (_connection == null) return indexes;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"PRAGMA index_list(\"{tableName.Replace("\"", "\"\"")}\");";

            using var reader = await cmd.ExecuteReaderAsync();
            var rawList = new List<(string Name, bool Unique, string Origin)>();
            while (await reader.ReadAsync())
            {
                // seq, name, unique, origin, partial
                var idxName = reader.GetString(1);
                var isUnique = reader.GetInt32(2) == 1;
                var origin = reader.IsDBNull(3) ? "c" : reader.GetString(3);
                rawList.Add((idxName, isUnique, origin));
            }

            foreach (var raw in rawList)
            {
                var colList = new List<string>();
                using var infoCmd = _connection.CreateCommand();
                infoCmd.CommandText = $"PRAGMA index_info(\"{raw.Name.Replace("\"", "\"\"")}\");";
                using var infoReader = await infoCmd.ExecuteReaderAsync();
                while (await infoReader.ReadAsync())
                {
                    // seqno, cid, name
                    if (!infoReader.IsDBNull(2))
                    {
                        colList.Add(infoReader.GetString(2));
                    }
                }

                string? ddl = null;
                using var sqlCmd = _connection.CreateCommand();
                sqlCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @name;";
                sqlCmd.Parameters.AddWithValue("@name", raw.Name);
                var ddlVal = await sqlCmd.ExecuteScalarAsync();
                if (ddlVal != null && ddlVal != DBNull.Value)
                {
                    ddl = ddlVal.ToString();
                }

                indexes.Add(new TableIndexInfo
                {
                    Name = raw.Name,
                    TableName = tableName,
                    Columns = string.Join(", ", colList),
                    IsUnique = raw.Unique,
                    IsPrimaryKey = string.Equals(raw.Origin, "pk", StringComparison.OrdinalIgnoreCase),
                    Method = "BTREE",
                    Definition = ddl
                });
            }
        }
        catch
        {
            // Ignore index discovery errors
        }

        return indexes;
    }

    public async Task<string> GetTableDdlAsync(string tableName)
    {
        if (_connection == null) return string.Empty;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name = @name;";
        cmd.Parameters.AddWithValue("@name", tableName);

        var val = await cmd.ExecuteScalarAsync();
        var tableDdl = val?.ToString() ?? $"-- No DDL found for {tableName}";

        try
        {
            using var idxCmd = _connection.CreateCommand();
            idxCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = @tbl AND sql IS NOT NULL;";
            idxCmd.Parameters.AddWithValue("@tbl", tableName);
            using var idxReader = await idxCmd.ExecuteReaderAsync();
            var idxList = new List<string>();
            while (await idxReader.ReadAsync())
            {
                if (!idxReader.IsDBNull(0))
                {
                    idxList.Add(idxReader.GetString(0));
                }
            }

            if (idxList.Count > 0)
            {
                tableDdl += "\n\n-- Indexes\n" + string.Join(";\n", idxList) + ";";
            }
        }
        catch
        {
            // Ignore
        }

        return tableDdl;
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        var fks = new List<ForeignKeyInfo>();
        if (_connection == null) return fks;

        var tablesToInspect = string.IsNullOrWhiteSpace(tableName)
            ? (await GetTablesAsync()).Where(t => !t.IsView).Select(t => t.Name).ToList()
            : new List<string> { tableName };

        foreach (var tbl in tablesToInspect)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"PRAGMA foreign_key_list(\"{tbl.Replace("\"", "\"\"")}\");";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // id, seq, table, from, to, on_update, on_delete, match
                var targetTable = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var fromCol = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var toCol = reader.IsDBNull(4) ? "id" : reader.GetString(4);
                if (string.IsNullOrWhiteSpace(toCol)) toCol = "id";
                var onUpdate = reader.IsDBNull(5) ? "NO ACTION" : reader.GetString(5);
                var onDelete = reader.IsDBNull(6) ? "NO ACTION" : reader.GetString(6);

                if (!string.IsNullOrWhiteSpace(fromCol) && !string.IsNullOrWhiteSpace(targetTable))
                {
                    fks.Add(new ForeignKeyInfo
                    {
                        ConstraintName = $"fk_{tbl}_{fromCol}",
                        SourceTable = tbl,
                        SourceColumn = fromCol,
                        TargetTable = targetTable,
                        TargetColumn = toCol,
                        OnUpdate = onUpdate,
                        OnDelete = onDelete
                    });
                }
            }
        }

        return fks;
    }

    public async Task<ErdGraphModel> GetErdGraphAsync()
    {
        var graph = new ErdGraphModel();
        if (_connection == null) return graph;

        var tables = await GetTablesAsync();
        var tablesOnly = tables.Where(t => !t.IsView).ToList();

        var tableColumns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var tableFks = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
        var uniqueColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tablesOnly)
        {
            var cols = await GetTableColumnsAsync(table.Name);
            tableColumns[table.Name] = cols;
            var fks = await GetForeignKeysAsync(table.Name);
            tableFks[table.Name] = fks;

            // Inspecionar colunas UNIQUE
            var uniqueCols = new List<string>();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"PRAGMA index_list(\"{table.Name.Replace("\"", "\"\"")}\");";
                using var reader = await cmd.ExecuteReaderAsync();
                var uniqueIndexes = new List<string>();
                while (await reader.ReadAsync())
                {
                    var isUnique = reader.GetInt32(2) == 1;
                    if (isUnique)
                    {
                        uniqueIndexes.Add(reader.GetString(1));
                    }
                }

                foreach (var idx in uniqueIndexes)
                {
                    using var idxCmd = _connection.CreateCommand();
                    idxCmd.CommandText = $"PRAGMA index_info(\"{idx.Replace("\"", "\"\"")}\");";
                    using var idxReader = await idxCmd.ExecuteReaderAsync();
                    while (await idxReader.ReadAsync())
                    {
                        uniqueCols.Add(idxReader.GetString(2));
                    }
                }
            }
            catch
            {
                // Ignora erro em SQLite restrito
            }
            uniqueColumns[table.Name] = uniqueCols;

            graph.Nodes.Add(new ErdTableNode
            {
                Table = table,
                Columns = cols
            });
        }

        foreach (var table in tablesOnly)
        {
            var fks = tableFks[table.Name];
            var cols = tableColumns[table.Name];
            var uniqueCols = uniqueColumns[table.Name];

            foreach (var fk in fks)
            {
                var card = ErdLayoutService.InferCardinality(fk, cols, fks, uniqueCols);
                graph.Relationships.Add(new ErdRelationshipLink
                {
                    SourceTable = fk.SourceTable,
                    SourceColumn = fk.SourceColumn,
                    TargetTable = fk.TargetTable,
                    TargetColumn = fk.TargetColumn,
                    Cardinality = card,
                    OnUpdate = fk.OnUpdate,
                    OnDelete = fk.OnDelete
                });
            }
        }

        // Aplica distribuição em colunas topológicas
        ErdLayoutService.ApplyTopologicalLayout(graph);

        // Carrega layout salvo se existir
        var dbId = string.IsNullOrWhiteSpace(Config.FilePath) ? Config.Name : Config.FilePath;
        ErdLayoutService.LoadLayout(dbId, graph);

        return graph;
    }

    public async Task<QueryResult> ExecuteBatchTransactionalAsync(
        IReadOnlyList<ParameterizedCommand> commands,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var result = new QueryResult();
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            result.Success = false;
            result.ErrorMessage = "Not connected to database.";
            return result;
        }

        if (commands.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return result;
        }

        var sw = Stopwatch.StartNew();
        SqliteTransaction? tx = null;
        try
        {
            tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            int totalAffected = 0;

            foreach (var cmdInfo in commands)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = cmdInfo.Sql;

                foreach (var param in cmdInfo.Parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }

                int affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (affected > 0)
                {
                    totalAffected += affected;
                }
            }

            await tx.CommitAsync(cancellationToken);
            sw.Stop();

            result.Success = true;
            result.RowsAffected = totalAffected;
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (tx != null)
            {
                try
                {
                    await tx.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Ignore secondary rollback errors
                }
            }

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        finally
        {
            if (tx != null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
