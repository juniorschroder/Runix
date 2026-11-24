using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Infrastructure.Security;

namespace Runix.Infrastructure.Data;

public class MySqlDatabaseService : IDatabaseService
{
    private MySqlConnection? _connection;
    private readonly ISshTunnelService _sshTunnelService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public ConnectionConfig Config { get; }
    public bool IsConnected => _connection != null && _connection.State == ConnectionState.Open;
    public CircuitState CurrentCircuitState => CircuitState.Closed;

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public MySqlDatabaseService(ConnectionConfig config, ISshTunnelService? sshTunnelService = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _sshTunnelService = sshTunnelService ?? new SshTunnelService();
    }

    public string BuildConnectionString(int? overridePort = null, string? overrideHost = null)
    {
        var host = !string.IsNullOrWhiteSpace(overrideHost)
            ? overrideHost
            : (string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host);

        var port = overridePort.HasValue
            ? (uint)overridePort.Value
            : (Config.Port > 0 ? (uint)Config.Port : 3306u);

        var sslMode = Config.SslMode?.Trim().ToLowerInvariant() switch
        {
            "disable" or "none" => MySqlSslMode.None,
            "require" or "required" => MySqlSslMode.Required,
            _ => MySqlSslMode.Preferred
        };

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            Database = string.IsNullOrWhiteSpace(Config.Database) ? "" : Config.Database,
            UserID = string.IsNullOrWhiteSpace(Config.Username) ? "root" : Config.Username,
            Password = Config.Password ?? "",
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 30,
            Pooling = true,
            PersistSecurityInfo = false,
            SslMode = sslMode,
            AllowUserVariables = true
        };

        return builder.ConnectionString;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            string connStr;
            if (Config.UseSshTunnel)
            {
                var localPort = await _sshTunnelService.EstablishTunnelAsync(Config);
                connStr = BuildConnectionString(overridePort: localPort, overrideHost: "127.0.0.1");
            }
            else
            {
                connStr = BuildConnectionString();
            }

            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync();
            return true;
        }
        finally
        {
            if (!IsConnected && _sshTunnelService.IsActive)
            {
                await _sshTunnelService.CloseTunnelAsync();
            }
        }
    }

    public async Task ConnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection != null)
            {
                await DisconnectInternalAsync();
            }

            string connStr;
            if (Config.UseSshTunnel)
            {
                var localPort = await _sshTunnelService.EstablishTunnelAsync(Config);
                connStr = BuildConnectionString(overridePort: localPort, overrideHost: "127.0.0.1");
            }
            else
            {
                connStr = BuildConnectionString();
            }

            _connection = new MySqlConnection(connStr);
            await _connection.OpenAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        if (_connection != null)
        {
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await _connection.CloseAsync();
                }
                await _connection.DisposeAsync();
            }
            catch
            {
                // Ignore disconnect exceptions
            }
            finally
            {
                _connection = null;
            }
        }

        if (_sshTunnelService.IsActive)
        {
            await _sshTunnelService.CloseTunnelAsync();
        }
    }

    public async Task<List<TableInfo>> GetTablesAsync()
    {
        var tables = new List<TableInfo>();
        EnsureConnected();

        const string sql = @"
            SELECT 
                table_name AS TableName,
                table_type AS TableType
            FROM information_schema.tables 
            WHERE table_schema = DATABASE()
            ORDER BY table_name;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var typeStr = reader.GetString(1);
            var isView = string.Equals(typeStr, "VIEW", StringComparison.OrdinalIgnoreCase);

            tables.Add(new TableInfo
            {
                Name = name,
                Schema = string.IsNullOrWhiteSpace(Config.Database) ? "mysql" : Config.Database,
                Type = isView ? "VIEW" : "BASE TABLE"
            });
        }

        return tables;
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        var columns = new List<ColumnInfo>();
        EnsureConnected();

        const string sql = @"
            SELECT 
                column_name AS ColumnName,
                data_type AS DataType,
                is_nullable AS IsNullable,
                column_default AS ColumnDefault,
                character_maximum_length AS CharMaxLength,
                extra AS Extra,
                column_key AS ColumnKey
            FROM information_schema.columns 
            WHERE table_schema = DATABASE() AND table_name = @tableName
            ORDER BY ordinal_position;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);
            var defaultValue = reader.IsDBNull(3) ? null : reader.GetString(3);
            var charMaxLength = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));
            var extra = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var colKey = reader.IsDBNull(6) ? "" : reader.GetString(6);

            columns.Add(new ColumnInfo
            {
                TableName = tableName,
                OrdinalPosition = columns.Count + 1,
                Name = colName,
                DataType = dataType,
                IsNullable = isNullable,
                DefaultValue = defaultValue,
                IsPrimaryKey = string.Equals(colKey, "PRI", StringComparison.OrdinalIgnoreCase)
            });
        }

        return columns;
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        var indexDict = new Dictionary<string, (bool isUnique, bool isPrimary, List<string> columns)>(StringComparer.OrdinalIgnoreCase);
        EnsureConnected();

        const string sql = @"
            SELECT 
                index_name AS IndexName,
                column_name AS ColumnName,
                non_unique AS NonUnique,
                seq_in_index AS SeqInIndex
            FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = @tableName
            ORDER BY index_name, seq_in_index;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var nonUnique = reader.GetInt32(2);
            var isUnique = nonUnique == 0;
            var isPrimary = string.Equals(indexName, "PRIMARY", StringComparison.OrdinalIgnoreCase);

            if (!indexDict.TryGetValue(indexName, out var entry))
            {
                entry = (isUnique, isPrimary, new List<string>());
                indexDict[indexName] = entry;
            }
            entry.columns.Add(columnName);
        }

        return indexDict.Select(kvp => new TableIndexInfo
        {
            Name = kvp.Key,
            TableName = tableName,
            IsUnique = kvp.Value.isUnique,
            IsPrimaryKey = kvp.Value.isPrimary,
            Columns = string.Join(", ", kvp.Value.columns)
        }).ToList();
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        var list = new List<ForeignKeyInfo>();
        EnsureConnected();

        var sql = @"
            SELECT 
                kcu.constraint_name AS ConstraintName,
                kcu.table_name AS TableName,
                kcu.column_name AS ColumnName,
                kcu.referenced_table_name AS ReferencedTableName,
                kcu.referenced_column_name AS ReferencedColumnName,
                COALESCE(rc.update_rule, 'NO ACTION') AS UpdateRule,
                COALESCE(rc.delete_rule, 'NO ACTION') AS DeleteRule
            FROM information_schema.key_column_usage kcu
            LEFT JOIN information_schema.referential_constraints rc 
                ON kcu.constraint_name = rc.constraint_name 
                AND kcu.constraint_schema = rc.constraint_schema
            WHERE kcu.constraint_schema = DATABASE()
              AND kcu.referenced_table_name IS NOT NULL";

        if (!string.IsNullOrWhiteSpace(tableName))
        {
            sql += " AND kcu.table_name = @tableName";
        }

        sql += " ORDER BY kcu.constraint_name, kcu.ordinal_position;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ForeignKeyInfo
            {
                ConstraintName = reader.GetString(0),
                SourceTable = reader.GetString(1),
                SourceColumn = reader.GetString(2),
                TargetTable = reader.GetString(3),
                TargetColumn = reader.GetString(4),
                OnUpdate = reader.GetString(5),
                OnDelete = reader.GetString(6)
            });
        }

        return list;
    }

    public async Task<ErdGraphModel> GetErdGraphAsync()
    {
        var tables = await GetTablesAsync();
        var tablesOnly = tables.Where(t => !t.IsView).ToList();
        var allFks = await GetForeignKeysAsync();
        var fksByTable = allFks
            .GroupBy(f => f.SourceTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var graph = new ErdGraphModel();
        var tableColumns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tbl in tablesOnly)
        {
            var cols = await GetTableColumnsAsync(tbl.Name);
            tableColumns[tbl.Name] = cols;

            if (fksByTable.TryGetValue(tbl.Name, out var tableFks))
            {
                foreach (var col in cols)
                {
                    var fk = tableFks.FirstOrDefault(f => string.Equals(f.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase));
                    if (fk != null)
                    {
                        col.IsForeignKey = true;
                        col.ForeignKeyReference = $"{fk.TargetTable}({fk.TargetColumn})";
                    }
                }
            }

            graph.Nodes.Add(new ErdTableNode
            {
                Table = tbl,
                Columns = cols
            });
        }

        foreach (var fk in allFks)
        {
            if (tableColumns.TryGetValue(fk.SourceTable, out var cols))
            {
                List<ForeignKeyInfo>? tableFks = null;
                fksByTable.TryGetValue(fk.SourceTable, out tableFks);
                var card = Runix.Core.Services.ErdLayoutService.InferCardinality(fk, cols, tableFks ?? new List<ForeignKeyInfo>());

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

        Runix.Core.Services.ErdLayoutService.ApplyTopologicalLayout(graph);
        return graph;
    }

    public async Task<string> GetTableDdlAsync(string tableName)
    {
        EnsureConnected();
        var quotedTable = QuoteIdentifier(tableName);

        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = $"SHOW CREATE TABLE {quotedTable};";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetString(1);
            }
        }
        catch
        {
            // Fallback: build synthetic DDL from column introspection
        }

        var cols = await GetTableColumnsAsync(tableName);
        var fks = await GetForeignKeysAsync(tableName);

        var colDefs = cols.Select(c =>
        {
            var nullStr = c.IsNullable ? "NULL" : "NOT NULL";
            var defaultStr = !string.IsNullOrWhiteSpace(c.DefaultValue) ? $" DEFAULT {c.DefaultValue}" : "";
            return $"  {QuoteIdentifier(c.Name)} {c.DataType} {nullStr}{defaultStr}";
        }).ToList();

        var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
        if (pkCols.Count > 0)
        {
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");
        }

        foreach (var fk in fks)
        {
            colDefs.Add($"  CONSTRAINT {QuoteIdentifier(fk.ConstraintName)} FOREIGN KEY ({QuoteIdentifier(fk.SourceColumn)}) REFERENCES {QuoteIdentifier(fk.TargetTable)} ({QuoteIdentifier(fk.TargetColumn)}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete}");
        }

        return $"CREATE TABLE {quotedTable} (\n{string.Join(",\n", colDefs)}\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        var result = new QueryResult();
        EnsureConnected();

        await _connectionLock.WaitAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                result.Columns.Add(reader.GetName(i));
            }

            int count = 0;
            while (await reader.ReadAsync(cancellationToken) && count < limit)
            {
                var row = new List<object?>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i));
                }
                result.Rows.Add(row);
                count++;
            }

            result.RowsAffected = reader.RecordsAffected;
            result.Success = true;
            sw.Stop();
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        var results = new List<QueryResult>();
        EnsureConnected();

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;

            var sw = Stopwatch.StartNew();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            do
            {
                var result = new QueryResult();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }

                int count = 0;
                while (await reader.ReadAsync(cancellationToken) && count < limit)
                {
                    var row = new List<object?>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i));
                    }
                    result.Rows.Add(row);
                    count++;
                }

                result.RowsAffected = reader.RecordsAffected;
                result.Success = true;
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                results.Add(result);
            } while (await reader.NextResultAsync(cancellationToken));

            return results;
        }
        catch (Exception ex)
        {
            results.Add(new QueryResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
            return results;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<QueryResult> GetTableDataAsync(
        string tableName,
        int offset = 0,
        int limit = 100,
        string? filter = null,
        string? sortColumn = null,
        bool sortAsc = true)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var sql = $"SELECT * FROM {quotedTable}";

        if (!string.IsNullOrWhiteSpace(filter))
        {
            sql += $" WHERE {filter}";
        }

        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            var dir = sortAsc ? "ASC" : "DESC";
            sql += $" ORDER BY {QuoteIdentifier(sortColumn)} {dir}";
        }

        sql += $" LIMIT {Math.Max(1, limit)} OFFSET {Math.Max(0, offset)};";

        return await ExecuteQueryAsync(sql);
    }

    public async Task<QueryResult> ExecuteBatchTransactionalAsync(
        IReadOnlyList<ParameterizedCommand> commands,
        CancellationToken cancellationToken = default)
    {
        var result = new QueryResult();
        EnsureConnected();

        if (commands.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return result;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        MySqlTransaction? tx = null;
        try
        {
            tx = await _connection!.BeginTransactionAsync(cancellationToken);
            int totalAffected = 0;

            foreach (var cmdInfo in commands)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = cmdInfo.Sql;

                foreach (var param in cmdInfo.Parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }

                totalAffected += await cmd.ExecuteNonQueryAsync(cancellationToken);
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
                try { await tx.RollbackAsync(cancellationToken); } catch { }
            }
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return "";
        var parts = identifier.Split('.');
        return string.Join(".", parts.Select(p => $"`{p.Trim('`').Replace("`", "``")}`"));
    }

    private void EnsureConnected()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Not connected to MySQL database.");
        }
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    _connection.Close();
                }
                _connection.Dispose();
            }
            catch
            {
                // Ignored
            }
            finally
            {
                _connection = null;
            }
        }

        if (_sshTunnelService.IsActive)
        {
            _sshTunnelService.CloseTunnelAsync().GetAwaiter().GetResult();
        }

        _connectionLock.Dispose();
    }
}
