using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Infrastructure.Security;

namespace Runix.Infrastructure.Data;

public class OracleDatabaseService : IDatabaseService
{
    private OracleConnection? _connection;
    private readonly ISshTunnelService _sshTunnelService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public ConnectionConfig Config { get; }
    public bool IsConnected => _connection != null && _connection.State == ConnectionState.Open;
    public CircuitState CurrentCircuitState => CircuitState.Closed;

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public OracleDatabaseService(ConnectionConfig config, ISshTunnelService? sshTunnelService = null)
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
            : (Config.Port > 0 ? Config.Port : 1521);

        string dataSource;
        if (Config.OracleConnectionType == OracleConnectionType.Sid)
        {
            var sid = !string.IsNullOrWhiteSpace(Config.OracleSid)
                ? Config.OracleSid
                : (!string.IsNullOrWhiteSpace(Config.Database) ? Config.Database : "XE");
            dataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SID={sid})))";
        }
        else if (Config.OracleConnectionType == OracleConnectionType.TnsDescriptor && !string.IsNullOrWhiteSpace(Config.OracleTnsDescriptor))
        {
            dataSource = Config.OracleTnsDescriptor;
        }
        else
        {
            var serviceName = !string.IsNullOrWhiteSpace(Config.OracleServiceName)
                ? Config.OracleServiceName
                : (!string.IsNullOrWhiteSpace(Config.Database) ? Config.Database : "ORCLPDB1");
            dataSource = $"{host}:{port}/{serviceName}";
        }

        var builder = new OracleConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = !string.IsNullOrWhiteSpace(Config.Username) ? Config.Username : "system",
            Password = Config.Password ?? "",
            ConnectionTimeout = 15,
            Pooling = true,
            PersistSecurityInfo = false
        };

        if (Config.OraclePrivilege == OraclePrivilege.SysDba)
        {
            builder["DBA Privilege"] = "SYSDBA";
        }
        else if (Config.OraclePrivilege == OraclePrivilege.SysOper)
        {
            builder["DBA Privilege"] = "SYSOPER";
        }

        return builder.ConnectionString;
    }

    public async Task ConnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (IsConnected) return;

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

            _connection = new OracleConnection(connStr);
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
            if (_connection != null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }

            if (_sshTunnelService.IsActive)
            {
                await _sshTunnelService.CloseTunnelAsync();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var diag = await TestConnectionDiagnosticsAsync();
            return diag.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ConnectionDiagnosticsResult> TestConnectionDiagnosticsAsync()
    {
        var sw = Stopwatch.StartNew();
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

            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();

            string versionString = "Oracle Database";
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT BANNER FROM V$VERSION WHERE ROWNUM = 1";
                cmd.CommandTimeout = 5;
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    versionString = result.ToString() ?? "Oracle Database";
                }
            }
            catch
            {
                // Fallback silencioso se V$VERSION não for acessível
            }

            sw.Stop();

            return new ConnectionDiagnosticsResult(
                Success: true,
                ErrorMessage: null,
                LatencyMs: sw.ElapsedMilliseconds,
                ServerVersion: versionString,
                EncryptionDetails: $"Connected to Oracle via {(Config.OracleConnectionType == OracleConnectionType.Sid ? "SID" : "Service Name")}"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionDiagnosticsResult(
                Success: false,
                ErrorMessage: ex.Message,
                LatencyMs: sw.ElapsedMilliseconds,
                ServerVersion: null,
                EncryptionDetails: null
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

    public async Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await EnsureConnectedAsync();
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 30;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<List<object?>>();
            int count = 0;
            while (await reader.ReadAsync(cancellationToken) && count < limit)
            {
                var row = new List<object?>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row.Add(val == DBNull.Value ? null : val);
                }
                rows.Add(row);
                count++;
            }

            sw.Stop();
            return new QueryResult
            {
                Success = true,
                Columns = columns,
                Rows = rows,
                AffectedRows = rows.Count,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new QueryResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds
            };
        }
    }

    public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        var statements = sql.Split(new[] { ";\r\n", ";\n", ";\r" }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();

        if (statements.Count == 0)
        {
            statements.Add(sql);
        }

        var results = new List<QueryResult>();
        foreach (var stmt in statements)
        {
            results.Add(await ExecuteQueryAsync(stmt, limit, cancellationToken));
        }
        return results;
    }

    public async Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var whereClause = string.IsNullOrWhiteSpace(filter) ? "" : $"WHERE {filter}";
        var orderClause = string.IsNullOrWhiteSpace(sortColumn) ? "" : $"ORDER BY {QuoteIdentifier(sortColumn)} {(sortAsc ? "ASC" : "DESC")}";

        // Oracle 12c+ standard offset pagination
        var sql = $@"
SELECT * FROM {quotedTable}
{whereClause}
{orderClause}
OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";

        return await ExecuteQueryAsync(sql);
    }

    private (string schema, string table) SplitSchemaAndTable(string tableName)
    {
        var raw = tableName.Replace("\"", "").Trim();
        var defaultOwner = !string.IsNullOrWhiteSpace(Config.Username)
            ? Config.Username.ToUpperInvariant()
            : "USER";

        if (raw.Contains('.'))
        {
            var parts = raw.Split('.');
            return (parts[0].ToUpperInvariant(), parts[1].ToUpperInvariant());
        }

        return (defaultOwner, raw.ToUpperInvariant());
    }

    public static string FormatOracleType(string rawType, int dataLength, int? precision, int? scale, int? charLength)
    {
        var upper = rawType.ToUpperInvariant().Trim();
        if (upper.StartsWith("VARCHAR2") || upper.StartsWith("NVARCHAR2") || upper.StartsWith("CHAR") || upper.StartsWith("NCHAR"))
        {
            var len = charLength.HasValue && charLength.Value > 0 ? charLength.Value : dataLength;
            return $"{upper}({len})";
        }

        if (upper == "NUMBER")
        {
            if (precision.HasValue && precision.Value > 0)
            {
                if (scale.HasValue && scale.Value > 0)
                {
                    return $"NUMBER({precision.Value},{scale.Value})";
                }
                return $"NUMBER({precision.Value})";
            }
            return "NUMBER";
        }

        if (upper.StartsWith("TIMESTAMP"))
        {
            if (scale.HasValue && scale.Value >= 0 && scale.Value != 6)
            {
                return $"{upper}({scale.Value})";
            }
            return upper;
        }

        if (upper == "RAW")
        {
            return $"RAW({dataLength})";
        }

        return upper;
    }

    public async Task<List<TableInfo>> GetTablesAsync()
    {
        var tables = new List<TableInfo>();
        await EnsureConnectedAsync();
        await _connectionLock.WaitAsync();
        try
        {
            var currentUser = (!string.IsNullOrWhiteSpace(Config.Username) ? Config.Username : "").Trim().ToUpperInvariant();

            const string sql = @"
                SELECT TABLE_NAME, OWNER, 'BASE TABLE' AS TABLE_TYPE, NUM_ROWS
                FROM ALL_TABLES
                WHERE (:owner = '' AND OWNER = USER) OR (:owner <> '' AND OWNER = :owner)
                UNION ALL
                SELECT VIEW_NAME AS TABLE_NAME, OWNER, 'VIEW' AS TABLE_TYPE, NULL AS NUM_ROWS
                FROM ALL_VIEWS
                WHERE (:owner = '' AND OWNER = USER) OR (:owner <> '' AND OWNER = :owner)
                ORDER BY TABLE_TYPE, OWNER, TABLE_NAME";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new OracleParameter("owner", currentUser));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tblName = reader.GetString(0);
                var owner = reader.GetString(1);
                var typeStr = reader.GetString(2);
                var numRows = reader.IsDBNull(3) ? (long?)null : Convert.ToInt64(reader.GetValue(3));

                var isView = string.Equals(typeStr, "VIEW", StringComparison.OrdinalIgnoreCase);

                var displayName = string.Equals(owner, currentUser, StringComparison.OrdinalIgnoreCase)
                    ? tblName
                    : $"{owner}.{tblName}";

                tables.Add(new TableInfo
                {
                    Name = displayName,
                    Schema = owner,
                    Type = isView ? "VIEW" : "BASE TABLE",
                    RowCount = numRows
                });
            }
        }
        catch
        {
            // Fallback para USER_TABLES e USER_VIEWS
            try
            {
                const string fallbackSql = @"
                    SELECT TABLE_NAME, USER AS OWNER, 'BASE TABLE' AS TABLE_TYPE, NUM_ROWS FROM USER_TABLES
                    UNION ALL
                    SELECT VIEW_NAME AS TABLE_NAME, USER AS OWNER, 'VIEW' AS TABLE_TYPE, NULL AS NUM_ROWS FROM USER_VIEWS
                    ORDER BY TABLE_TYPE, TABLE_NAME";

                await using var cmd = _connection!.CreateCommand();
                cmd.CommandText = fallbackSql;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var tblName = reader.GetString(0);
                    var owner = reader.GetString(1);
                    var typeStr = reader.GetString(2);
                    var numRows = reader.IsDBNull(3) ? (long?)null : Convert.ToInt64(reader.GetValue(3));

                    tables.Add(new TableInfo
                    {
                        Name = tblName,
                        Schema = owner,
                        Type = string.Equals(typeStr, "VIEW", StringComparison.OrdinalIgnoreCase) ? "VIEW" : "BASE TABLE",
                        RowCount = numRows
                    });
                }
            }
            catch
            {
                // Ignorar erro de fallback e manter lista vazia
            }
        }
        finally
        {
            _connectionLock.Release();
        }

        return tables;
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        var columns = new List<ColumnInfo>();
        await EnsureConnectedAsync();
        await _connectionLock.WaitAsync();
        try
        {
            var (owner, table) = SplitSchemaAndTable(tableName);

            // 1. Fetch Primary Keys
            var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                const string pkSql = @"
                    SELECT acc.COLUMN_NAME
                    FROM ALL_CONSTRAINTS ac
                    JOIN ALL_CONS_COLUMNS acc ON ac.OWNER = acc.OWNER AND ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
                    WHERE ac.OWNER = :owner AND ac.TABLE_NAME = :tableName AND ac.CONSTRAINT_TYPE = 'P'
                    ORDER BY acc.POSITION";

                await using var pkCmd = _connection!.CreateCommand();
                pkCmd.CommandText = pkSql;
                pkCmd.Parameters.Add(new OracleParameter("owner", owner));
                pkCmd.Parameters.Add(new OracleParameter("tableName", table));

                await using var pkReader = await pkCmd.ExecuteReaderAsync();
                while (await pkReader.ReadAsync())
                {
                    pkColumns.Add(pkReader.GetString(0));
                }
            }
            catch
            {
                // Fallback gracioso
            }

            // 2. Fetch Foreign Keys for Table
            var fkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                const string fkSql = @"
                    SELECT acc.COLUMN_NAME, r_ac.TABLE_NAME, r_acc.COLUMN_NAME
                    FROM ALL_CONSTRAINTS ac
                    JOIN ALL_CONS_COLUMNS acc ON ac.OWNER = acc.OWNER AND ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
                    JOIN ALL_CONSTRAINTS r_ac ON ac.R_OWNER = r_ac.OWNER AND ac.R_CONSTRAINT_NAME = r_ac.CONSTRAINT_NAME
                    JOIN ALL_CONS_COLUMNS r_acc ON r_ac.OWNER = r_acc.OWNER AND r_ac.CONSTRAINT_NAME = r_acc.CONSTRAINT_NAME AND acc.POSITION = r_acc.POSITION
                    WHERE ac.OWNER = :owner AND ac.TABLE_NAME = :tableName AND ac.CONSTRAINT_TYPE = 'R'";

                await using var fkCmd = _connection!.CreateCommand();
                fkCmd.CommandText = fkSql;
                fkCmd.Parameters.Add(new OracleParameter("owner", owner));
                fkCmd.Parameters.Add(new OracleParameter("tableName", table));

                await using var fkReader = await fkCmd.ExecuteReaderAsync();
                while (await fkReader.ReadAsync())
                {
                    var colName = fkReader.GetString(0);
                    var tgtTable = fkReader.GetString(1);
                    var tgtCol = fkReader.GetString(2);
                    fkMap[colName] = $"{tgtTable}({tgtCol})";
                }
            }
            catch
            {
                // Fallback gracioso
            }

            // 3. Fetch Columns
            const string colSql = @"
                SELECT c.COLUMN_ID, c.COLUMN_NAME, c.DATA_TYPE, c.DATA_LENGTH, c.DATA_PRECISION, c.DATA_SCALE, 
                       c.NULLABLE, c.DATA_DEFAULT, c.CHAR_LENGTH
                FROM ALL_TAB_COLUMNS c
                WHERE c.OWNER = :owner AND c.TABLE_NAME = :tableName
                ORDER BY c.COLUMN_ID";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = colSql;
            cmd.Parameters.Add(new OracleParameter("owner", owner));
            cmd.Parameters.Add(new OracleParameter("tableName", table));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var colId = reader.IsDBNull(0) ? columns.Count + 1 : Convert.ToInt32(reader.GetValue(0));
                var colName = reader.GetString(1);
                var rawType = reader.GetString(2);
                var dataLength = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                int? precision = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
                int? scale = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
                var nullableStr = reader.GetString(6);
                var defaultVal = reader.IsDBNull(7) ? null : reader.GetValue(7)?.ToString()?.Trim();
                int? charLength = reader.IsDBNull(8) ? null : Convert.ToInt32(reader.GetValue(8));

                var isPk = pkColumns.Contains(colName);
                var isFk = fkMap.TryGetValue(colName, out var fkRef);

                var formattedType = FormatOracleType(rawType, dataLength, precision, scale, charLength);

                columns.Add(new ColumnInfo
                {
                    TableName = tableName,
                    OrdinalPosition = colId,
                    Name = colName,
                    DataType = formattedType,
                    IsNullable = string.Equals(nullableStr, "Y", StringComparison.OrdinalIgnoreCase),
                    IsPrimaryKey = isPk,
                    IsForeignKey = isFk,
                    ForeignKeyReference = fkRef,
                    DefaultValue = defaultVal
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OracleDatabaseService] Error in GetTableColumnsAsync: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }

        return columns;
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        var list = new List<TableIndexInfo>();
        await EnsureConnectedAsync();
        await _connectionLock.WaitAsync();
        try
        {
            var (owner, table) = SplitSchemaAndTable(tableName);

            const string sql = @"
                SELECT 
                    ai.INDEX_NAME,
                    ai.UNIQUENESS,
                    aic.COLUMN_NAME,
                    aic.COLUMN_POSITION,
                    ai.INDEX_TYPE
                FROM ALL_INDEXES ai
                JOIN ALL_IND_COLUMNS aic ON ai.OWNER = aic.INDEX_OWNER AND ai.INDEX_NAME = aic.INDEX_NAME
                WHERE ai.TABLE_OWNER = :owner AND ai.TABLE_NAME = :tableName
                ORDER BY ai.INDEX_NAME, aic.COLUMN_POSITION";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new OracleParameter("owner", owner));
            cmd.Parameters.Add(new OracleParameter("tableName", table));

            var indexDict = new Dictionary<string, (string uniqueness, string type, List<string> cols)>(StringComparer.OrdinalIgnoreCase);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var indexName = reader.GetString(0);
                var uniqueness = reader.GetString(1);
                var colName = reader.GetString(2);
                var idxType = reader.IsDBNull(4) ? "NORMAL" : reader.GetString(4);

                if (!indexDict.TryGetValue(indexName, out var entry))
                {
                    entry = (uniqueness, idxType, new List<string>());
                    indexDict[indexName] = entry;
                }
                entry.cols.Add(colName);
            }

            foreach (var (idxName, entry) in indexDict)
            {
                var isUnique = string.Equals(entry.uniqueness, "UNIQUE", StringComparison.OrdinalIgnoreCase);
                list.Add(new TableIndexInfo
                {
                    Name = idxName,
                    TableName = tableName,
                    Columns = string.Join(", ", entry.cols),
                    IsUnique = isUnique,
                    IsPrimaryKey = idxName.StartsWith("PK_", StringComparison.OrdinalIgnoreCase) || isUnique,
                    Method = entry.type,
                    Definition = $"CREATE {(isUnique ? "UNIQUE " : "")}INDEX \"{idxName}\" ON \"{owner}\".\"{table}\" ({string.Join(", ", entry.cols.Select(QuoteIdentifier))})"
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OracleDatabaseService] Error in GetTableIndexesAsync: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }

        return list;
    }

    public async Task<string> GetTableDdlAsync(string tableName)
    {
        var cols = await GetTableColumnsAsync(tableName);
        var fks = await GetForeignKeysAsync(tableName);
        var (owner, table) = SplitSchemaAndTable(tableName);
        var quotedTable = $"\"{owner}\".\"{table}\"";

        var lines = new List<string>();

        foreach (var c in cols)
        {
            var nullability = c.IsNullable ? "" : " NOT NULL";
            var def = !string.IsNullOrWhiteSpace(c.DefaultValue) ? $" DEFAULT {c.DefaultValue.Trim()}" : "";
            lines.Add($"    \"{c.Name}\" {c.DataType}{def}{nullability}");
        }

        var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => $"\"{c.Name}\"").ToList();
        if (pkCols.Count > 0)
        {
            lines.Add($"    CONSTRAINT \"PK_{table}\" PRIMARY KEY ({string.Join(", ", pkCols)})");
        }

        foreach (var fk in fks)
        {
            var onDel = fk.OnDelete.Equals("CASCADE", StringComparison.OrdinalIgnoreCase) ? " ON DELETE CASCADE" : "";
            lines.Add($"    CONSTRAINT \"{fk.ConstraintName}\" FOREIGN KEY (\"{fk.SourceColumn}\") REFERENCES \"{fk.TargetTable}\" (\"{fk.TargetColumn}\"){onDel}");
        }

        return $"CREATE TABLE {quotedTable} (\n{string.Join(",\n", lines)}\n);\n";
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        var list = new List<ForeignKeyInfo>();
        await EnsureConnectedAsync();
        await _connectionLock.WaitAsync();
        try
        {
            string? owner = null;
            string? table = null;
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                var parts = SplitSchemaAndTable(tableName);
                owner = parts.schema;
                table = parts.table;
            }
            else
            {
                owner = !string.IsNullOrWhiteSpace(Config.Username) ? Config.Username.ToUpperInvariant() : "USER";
            }

            const string sql = @"
                SELECT 
                    ac.CONSTRAINT_NAME,
                    ac.TABLE_NAME AS SOURCE_TABLE,
                    acc.COLUMN_NAME AS SOURCE_COLUMN,
                    r_ac.TABLE_NAME AS TARGET_TABLE,
                    r_acc.COLUMN_NAME AS TARGET_COLUMN,
                    ac.DELETE_RULE
                FROM ALL_CONSTRAINTS ac
                JOIN ALL_CONS_COLUMNS acc ON ac.OWNER = acc.OWNER AND ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
                JOIN ALL_CONSTRAINTS r_ac ON ac.R_OWNER = r_ac.OWNER AND ac.R_CONSTRAINT_NAME = r_ac.CONSTRAINT_NAME
                JOIN ALL_CONS_COLUMNS r_acc ON r_ac.OWNER = r_acc.OWNER AND r_ac.CONSTRAINT_NAME = r_acc.CONSTRAINT_NAME AND acc.POSITION = r_acc.POSITION
                WHERE ac.OWNER = :owner
                  AND (:tableName = '' OR ac.TABLE_NAME = :tableName)
                  AND ac.CONSTRAINT_TYPE = 'R'
                ORDER BY ac.CONSTRAINT_NAME, acc.POSITION";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new OracleParameter("owner", owner));
            cmd.Parameters.Add(new OracleParameter("tableName", table ?? ""));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var constraintName = reader.GetString(0);
                var srcTable = reader.GetString(1);
                var srcCol = reader.GetString(2);
                var tgtTable = reader.GetString(3);
                var tgtCol = reader.GetString(4);
                var delRule = reader.IsDBNull(5) ? "NO ACTION" : reader.GetString(5);

                list.Add(new ForeignKeyInfo
                {
                    ConstraintName = constraintName,
                    SourceTable = srcTable,
                    SourceColumn = srcCol,
                    TargetTable = tgtTable,
                    TargetColumn = tgtCol,
                    OnDelete = delRule,
                    OnUpdate = "NO ACTION"
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OracleDatabaseService] Error in GetForeignKeysAsync: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }

        return list;
    }

    public async Task<ErdGraphModel> GetErdGraphAsync()
    {
        var graph = new ErdGraphModel();
        var tables = await GetTablesAsync();
        var tablesOnly = tables.Where(t => !t.IsView).ToList();
        if (tablesOnly.Count == 0) return graph;

        var allFks = await GetForeignKeysAsync();
        var fksByTable = allFks
            .GroupBy(f => f.SourceTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

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
            tableColumns.TryGetValue(fk.SourceTable, out var cols);
            cols ??= new List<ColumnInfo>();

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
                OnDelete = fk.OnDelete,
                OnUpdate = fk.OnUpdate
            });
        }

        Runix.Core.Services.ErdLayoutService.ApplyTopologicalLayout(graph);
        return graph;
    }

    public async Task<QueryResult> ExecuteBatchTransactionalAsync(IReadOnlyList<ParameterizedCommand> commands, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await EnsureConnectedAsync();
        await using var tx = await _connection!.BeginTransactionAsync(cancellationToken);
        try
        {
            int totalAffected = 0;
            foreach (var cmdInfo in commands)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = (OracleTransaction)tx;
                cmd.CommandText = cmdInfo.Sql;
                cmd.CommandTimeout = 30;

                if (cmdInfo.Parameters != null)
                {
                    foreach (var (k, v) in cmdInfo.Parameters)
                    {
                        var paramName = k.StartsWith(":") || k.StartsWith("@") ? k.Substring(1) : k;
                        cmd.Parameters.Add(new OracleParameter(paramName, v ?? DBNull.Value));
                    }
                }

                totalAffected += await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            sw.Stop();

            return new QueryResult
            {
                Success = true,
                AffectedRows = totalAffected,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            sw.Stop();
            return new QueryResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds
            };
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (!IsConnected)
        {
            await ConnectAsync();
        }
    }

    public static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return identifier;
        if (identifier.Contains("."))
        {
            var parts = identifier.Split('.');
            return string.Join(".", parts.Select(p => $"\"{p.Trim('\"')}\""));
        }
        return $"\"{identifier.Trim('\"')}\"";
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            try
            {
                _connection.Close();
                _connection.Dispose();
            }
            catch
            {
                // Dispose silencioso
            }
            _connection = null;
        }

        if (_sshTunnelService.IsActive)
        {
            try
            {
                _sshTunnelService.CloseTunnelAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignorar
            }
        }
    }
}
