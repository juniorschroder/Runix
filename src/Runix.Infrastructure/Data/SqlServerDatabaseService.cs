using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Infrastructure.Security;

namespace Runix.Infrastructure.Data;

public class SqlServerDatabaseService : IDatabaseService
{
    private SqlConnection? _connection;
    private readonly ISshTunnelService _sshTunnelService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public ConnectionConfig Config { get; }
    public bool IsConnected => _connection != null && _connection.State == ConnectionState.Open;
    public CircuitState CurrentCircuitState => CircuitState.Closed;

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public SqlServerDatabaseService(ConnectionConfig config, ISshTunnelService? sshTunnelService = null)
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
            ? overridePort.Value
            : (Config.Port > 0 ? Config.Port : 1433);

        var dataSource = port == 1433 ? host : $"{host},{port}";

        var sslMode = Config.SslMode?.Trim().ToLowerInvariant();
        var encryptOption = sslMode switch
        {
            "require" or "required" => SqlConnectionEncryptOption.Mandatory,
            "disable" or "none" => SqlConnectionEncryptOption.Optional,
            _ => SqlConnectionEncryptOption.Optional
        };

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = string.IsNullOrWhiteSpace(Config.Database) ? "master" : Config.Database,
            UserID = string.IsNullOrWhiteSpace(Config.Username) ? "sa" : Config.Username,
            Password = Config.Password ?? "",
            ConnectTimeout = 15,
            CommandTimeout = 30,
            Pooling = true,
            PersistSecurityInfo = false,
            TrustServerCertificate = Config.TrustServerCertificate,
            Encrypt = encryptOption
        };

        return builder.ConnectionString;
    }

    public async Task<bool> TestConnectionAsync()
    {
        var result = await TestConnectionDiagnosticsAsync();
        if (!result.Success && result.ErrorMessage != null)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }
        return result.Success;
    }

    public async Task<ConnectionDiagnosticsResult> TestConnectionDiagnosticsAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
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

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR) + ' (' + CAST(SERVERPROPERTY('ProductLevel') AS NVARCHAR) + ')';";
            var versionObj = await cmd.ExecuteScalarAsync();
            sw.Stop();
            string version = versionObj?.ToString() ?? "Unknown";
            return new ConnectionDiagnosticsResult(
                Success: true,
                LatencyMs: sw.ElapsedMilliseconds,
                ServerVersion: $"SQL Server {version}",
                EncryptionDetails: Config.TrustServerCertificate ? "TLS (TrustServerCert)" : "TLS Encrypted"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionDiagnosticsResult(
                Success: false,
                ErrorMessage: ex.Message,
                LatencyMs: sw.ElapsedMilliseconds
            );
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

            _connection = new SqlConnection(connStr);
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
                // Ignored
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
                s.name AS SchemaName,
                t.name AS TableName,
                'BASE TABLE' AS TableType
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 
                s.name AS SchemaName,
                v.name AS TableName,
                'VIEW' AS TableType
            FROM sys.views v
            JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, TableName;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var typeStr = reader.GetString(2);
            var isView = string.Equals(typeStr, "VIEW", StringComparison.OrdinalIgnoreCase);

            var fullName = string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{schema}.{name}";

            tables.Add(new TableInfo
            {
                Name = fullName,
                Schema = schema,
                Type = isView ? "VIEW" : "BASE TABLE"
            });
        }

        return tables;
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        var columns = new List<ColumnInfo>();
        EnsureConnected();

        var (schema, table) = SplitSchemaAndTable(tableName);

        const string sql = @"
            SELECT 
                c.name AS ColumnName,
                ty.name AS DataType,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                c.max_length AS MaxLength,
                dc.definition AS DefaultValue,
                CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
            FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.index_columns ic
                JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY c.column_id;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullable = reader.GetBoolean(2);
            var isIdentity = reader.GetBoolean(3);
            var maxLength = reader.GetInt16(4);
            var defaultValue = reader.IsDBNull(5) ? null : reader.GetString(5);
            var isPrimaryKey = reader.GetInt32(6) == 1;

            columns.Add(new ColumnInfo
            {
                TableName = tableName,
                OrdinalPosition = columns.Count + 1,
                Name = colName,
                DataType = dataType,
                IsNullable = isNullable,
                DefaultValue = defaultValue,
                IsPrimaryKey = isPrimaryKey
            });
        }

        return columns;
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        var indexDict = new Dictionary<string, (bool isUnique, bool isPrimary, List<string> columns)>(StringComparer.OrdinalIgnoreCase);
        EnsureConnected();

        var (schema, table) = SplitSchemaAndTable(tableName);

        const string sql = @"
            SELECT 
                i.name AS IndexName,
                c.name AS ColumnName,
                i.is_unique AS IsUnique,
                i.is_primary_key AS IsPrimaryKey,
                ic.key_ordinal AS KeyOrdinal
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE s.name = @schema AND t.name = @table AND i.name IS NOT NULL
            ORDER BY i.name, ic.key_ordinal;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(0);
            var colName = reader.GetString(1);
            var isUnique = reader.GetBoolean(2);
            var isPrimary = reader.GetBoolean(3);

            if (!indexDict.TryGetValue(indexName, out var entry))
            {
                entry = (isUnique, isPrimary, new List<string>());
                indexDict[indexName] = entry;
            }
            entry.columns.Add(colName);
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
                fk.name AS ConstraintName,
                OBJECT_NAME(fk.parent_object_id) AS TableName,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                OBJECT_NAME(fk.referenced_object_id) AS ReferencedTableName,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumnName,
                REPLACE(fk.update_referential_action_desc, '_', ' ') AS UpdateRule,
                REPLACE(fk.delete_referential_action_desc, '_', ' ') AS DeleteRule
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id";

        if (!string.IsNullOrWhiteSpace(tableName))
        {
            var (_, table) = SplitSchemaAndTable(tableName);
            sql += " WHERE OBJECT_NAME(fk.parent_object_id) = @table";
        }

        sql += " ORDER BY fk.name, fkc.constraint_column_id;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            var (_, table) = SplitSchemaAndTable(tableName);
            cmd.Parameters.AddWithValue("@table", table);
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
        var cols = await GetTableColumnsAsync(tableName);
        var fks = await GetForeignKeysAsync(tableName);
        var quotedTable = QuoteIdentifier(tableName);

        var colDefs = cols.Select(c =>
        {
            var nullStr = c.IsNullable ? "NULL" : "NOT NULL";
            var defaultStr = !string.IsNullOrWhiteSpace(c.DefaultValue) ? $" DEFAULT {c.DefaultValue}" : "";
            return $"  {QuoteIdentifier(c.Name)} [{c.DataType}] {nullStr}{defaultStr}";
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

        return $"CREATE TABLE {quotedTable} (\n{string.Join(",\n", colDefs)}\n);";
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

        var orderClause = !string.IsNullOrWhiteSpace(sortColumn)
            ? $"ORDER BY {QuoteIdentifier(sortColumn)} {(sortAsc ? "ASC" : "DESC")}"
            : "ORDER BY (SELECT NULL)";

        sql += $" {orderClause} OFFSET {Math.Max(0, offset)} ROWS FETCH NEXT {Math.Max(1, limit)} ROWS ONLY;";

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
        SqlTransaction? tx = null;
        try
        {
            tx = (SqlTransaction)await _connection!.BeginTransactionAsync(cancellationToken);
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
        return string.Join(".", parts.Select(p => $"[{p.Trim('[', ']').Replace("]", "]]")}]"));
    }

    private static (string schema, string table) SplitSchemaAndTable(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("dbo", "");
        var parts = fullName.Split('.');
        if (parts.Length > 1)
        {
            return (parts[0].Trim('[', ']'), parts[1].Trim('[', ']'));
        }
        return ("dbo", parts[0].Trim('[', ']'));
    }

    private void EnsureConnected()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Not connected to Microsoft SQL Server database.");
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
