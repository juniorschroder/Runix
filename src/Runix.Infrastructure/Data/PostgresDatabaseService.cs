using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Core.Services;
using Runix.Infrastructure.Security;

namespace Runix.Infrastructure.Data;

public class PostgresDatabaseService : IDatabaseService
{
    private NpgsqlConnection? _connection;
    private readonly ISshTunnelService _sshTunnelService;
    public ConnectionConfig Config { get; }
    public bool IsConnected => _connection != null && _connection.State == System.Data.ConnectionState.Open;
    public CircuitState CurrentCircuitState => CircuitState.Closed;

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public PostgresDatabaseService(ConnectionConfig config, ISshTunnelService? sshTunnelService = null)
    {
        Config = config;
        _sshTunnelService = sshTunnelService ?? new SshTunnelService();
    }

    private string BuildConnectionString(int? overridePort = null, string? overrideHost = null)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = !string.IsNullOrWhiteSpace(overrideHost)
                ? overrideHost
                : (string.IsNullOrWhiteSpace(Config.Host) ? "localhost" : Config.Host),
            Port = overridePort ?? (Config.Port > 0 ? Config.Port : 5432),
            Database = string.IsNullOrWhiteSpace(Config.Database) ? "postgres" : Config.Database,
            Username = string.IsNullOrWhiteSpace(Config.Username) ? "postgres" : Config.Username,
            Password = Config.Password,
            Timeout = 10,
            CommandTimeout = 30
        };

        if (Config.SslMode.Equals("Disable", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = Npgsql.SslMode.Disable;
        else if (Config.SslMode.Equals("Require", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = Npgsql.SslMode.Require;
        else
            builder.SslMode = Npgsql.SslMode.Prefer;

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

            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch (Exception)
        {
            throw;
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
        if (_connection != null)
        {
            await DisconnectAsync();
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

        _connection = new NpgsqlConnection(connStr);
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

        if (_sshTunnelService.IsActive)
        {
            await _sshTunnelService.CloseTunnelAsync();
        }
    }

    public async Task<List<TableInfo>> GetTablesAsync()
    {
        var tables = new List<TableInfo>();
        if (_connection == null) return tables;

        const string sql = @"
            SELECT table_name, table_schema, table_type
            FROM information_schema.tables 
            WHERE table_schema NOT IN ('information_schema', 'pg_catalog') 
            ORDER BY table_type, table_schema, table_name;";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var schema = reader.GetString(1);
            var rawType = reader.GetString(2);
            var type = rawType.Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? "view" : "table";

            tables.Add(new TableInfo
            {
                Name = name,
                Schema = schema,
                Type = type
            });
        }

        // Fast row count estimate from pg_class
        foreach (var tbl in tables)
        {
            if (tbl.Type == "table")
            {
                try
                {
                    using var countCmd = _connection.CreateCommand();
                    countCmd.CommandText = "SELECT reltuples::bigint FROM pg_class WHERE relname = @name;";
                    countCmd.Parameters.AddWithValue("@name", tbl.Name);
                    var count = await countCmd.ExecuteScalarAsync();
                    if (count != null && count != DBNull.Value)
                    {
                        var c = Convert.ToInt64(count);
                        tbl.RowCount = c >= 0 ? c : -1;
                    }
                }
                catch
                {
                    // Ignore
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
                new() { Success = false, ErrorMessage = "Not connected to PostgreSQL database." }
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

        string? schemaName = null;
        string cleanTable = tableName;
        if (tableName.Contains('.'))
        {
            var parts = tableName.Split('.', 2);
            schemaName = parts[0].Trim('"');
            cleanTable = parts[1].Trim('"');
        }
        else
        {
            cleanTable = tableName.Trim('"');
        }

        // 1. Direct pg_catalog introspection for exact data types, defaults and PKs
        const string catalogSql = @"
            SELECT
                a.attnum AS ordinal_position,
                a.attname AS column_name,
                format_type(a.atttypid, a.atttypmod) AS data_type,
                NOT a.attnotnull AS is_nullable,
                pg_get_expr(d.adbin, d.adrelid) AS column_default,
                COALESCE(pk.is_pk, false) AS is_primary_key
            FROM pg_attribute a
            JOIN pg_class c ON a.attrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            LEFT JOIN pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum
            LEFT JOIN (
                SELECT conrelid, unnest(conkey) AS attnum, true AS is_pk
                FROM pg_constraint
                WHERE contype = 'p'
            ) pk ON pk.conrelid = c.oid AND pk.attnum = a.attnum
            WHERE (c.relname = @tableName OR c.relname = LOWER(@tableName))
              AND (CAST(@schemaName AS text) IS NULL OR n.nspname = @schemaName)
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY a.attnum;";

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = catalogSql;
            cmd.Parameters.Add(new NpgsqlParameter("tableName", NpgsqlTypes.NpgsqlDbType.Text) { Value = cleanTable });
            cmd.Parameters.Add(new NpgsqlParameter("schemaName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schemaName ?? DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var pos = reader.GetInt32(0);
                var colName = reader.GetString(1);
                var type = reader.GetString(2);
                var isNullable = reader.GetBoolean(3);
                var defVal = reader.IsDBNull(4) ? null : reader.GetString(4);
                var isPk = reader.GetBoolean(5);

                columns.Add(new ColumnInfo
                {
                    OrdinalPosition = pos,
                    Name = colName,
                    DataType = type,
                    IsNullable = isNullable,
                    IsPrimaryKey = isPk,
                    DefaultValue = defVal
                });
            }
        }
        catch
        {
            // Fallback to information_schema if restricted catalog permissions
        }

        if (columns.Count == 0)
        {
            const string fallbackSql = @"
                SELECT 
                    c.ordinal_position,
                    c.column_name,
                    c.data_type,
                    c.is_nullable,
                    c.column_default,
                    COALESCE((
                        SELECT true 
                        FROM information_schema.table_constraints tc
                        JOIN information_schema.key_column_usage kcu 
                          ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                        WHERE tc.constraint_type = 'PRIMARY KEY' 
                          AND (tc.table_name = c.table_name OR tc.table_name = LOWER(c.table_name))
                          AND tc.table_schema = c.table_schema 
                          AND kcu.column_name = c.column_name
                        LIMIT 1
                    ), false) AS is_pk
                FROM information_schema.columns c
                WHERE (c.table_name = @tableName OR c.table_name = LOWER(@tableName))
                  AND (CAST(@schemaName AS text) IS NULL OR c.table_schema = @schemaName)
                ORDER BY c.ordinal_position;";

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = fallbackSql;
                cmd.Parameters.Add(new NpgsqlParameter("tableName", NpgsqlTypes.NpgsqlDbType.Text) { Value = cleanTable });
                cmd.Parameters.Add(new NpgsqlParameter("schemaName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schemaName ?? DBNull.Value });

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var pos = reader.GetInt32(0);
                    var colName = reader.GetString(1);
                    var type = reader.GetString(2);
                    var isNullable = reader.GetString(3).Equals("YES", StringComparison.OrdinalIgnoreCase);
                    var defVal = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var isPk = reader.GetBoolean(5);

                    columns.Add(new ColumnInfo
                    {
                        OrdinalPosition = pos,
                        Name = colName,
                        DataType = type,
                        IsNullable = isNullable,
                        IsPrimaryKey = isPk,
                        DefaultValue = defVal
                    });
                }
            }
            catch
            {
                // Ignore fallback error
            }
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
            // Ignore FK correlation error
        }

        return columns;
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        var indexes = new List<TableIndexInfo>();
        if (_connection == null) return indexes;

        string? schemaName = null;
        string cleanTable = tableName;
        if (tableName.Contains('.'))
        {
            var parts = tableName.Split('.', 2);
            schemaName = parts[0].Trim('"');
            cleanTable = parts[1].Trim('"');
        }
        else
        {
            cleanTable = tableName.Trim('"');
        }

        const string sql = @"
            SELECT
                i.relname AS index_name,
                t.relname AS table_name,
                string_agg(COALESCE(a.attname, pg_get_indexdef(idx.indexrelid, k.ord::integer, true)), ', ' ORDER BY k.ord) AS column_names,
                idx.indisunique AS is_unique,
                idx.indisprimary AS is_primary,
                UPPER(am.amname) AS index_method,
                pg_get_indexdef(idx.indexrelid) AS index_def
            FROM pg_index idx
            JOIN pg_class i ON i.oid = idx.indexrelid
            JOIN pg_class t ON t.oid = idx.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_am am ON am.oid = i.relam
            CROSS JOIN LATERAL unnest(idx.indkey) WITH ORDINALITY AS k(attnum, ord)
            LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE (t.relname = @tableName OR t.relname = LOWER(@tableName))
              AND (CAST(@schemaName AS text) IS NULL OR n.nspname = @schemaName)
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            GROUP BY i.relname, t.relname, idx.indisunique, idx.indisprimary, am.amname, idx.indexrelid
            ORDER BY idx.indisprimary DESC, idx.indisunique DESC, i.relname;";

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("tableName", NpgsqlTypes.NpgsqlDbType.Text) { Value = cleanTable });
            cmd.Parameters.Add(new NpgsqlParameter("schemaName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schemaName ?? DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var idxName = reader.GetString(0);
                var tblName = reader.GetString(1);
                var colNames = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var isUnique = reader.GetBoolean(3);
                var isPrimary = reader.GetBoolean(4);
                var method = reader.GetString(5);
                var def = reader.IsDBNull(6) ? null : reader.GetString(6);

                indexes.Add(new TableIndexInfo
                {
                    Name = idxName,
                    TableName = tblName,
                    Columns = colNames,
                    IsUnique = isUnique,
                    IsPrimaryKey = isPrimary,
                    Method = method,
                    Definition = def
                });
            }
        }
        catch
        {
            // Ignore index query errors on older pg or views
        }

        return indexes;
    }

    public async Task<string> GetTableDdlAsync(string tableName)
    {
        var cols = await GetTableColumnsAsync(tableName);
        if (cols.Count == 0) return $"-- No DDL found for table {tableName}";

        var fks = await GetForeignKeysAsync(tableName);
        var indexes = await GetTableIndexesAsync(tableName);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"CREATE TABLE \"{tableName}\" (");

        var pkCols = cols.Where(c => c.IsPrimaryKey).Select(c => $"\"{c.Name}\"").ToList();
        bool hasConstraints = pkCols.Count > 0 || fks.Count > 0;

        for (int i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            sb.Append($"    \"{col.Name}\" {col.DataType}");
            if (!col.IsNullable) sb.Append(" NOT NULL");
            if (!string.IsNullOrEmpty(col.DefaultValue)) sb.Append($" DEFAULT {col.DefaultValue}");

            if (i < cols.Count - 1 || hasConstraints)
            {
                sb.AppendLine(",");
            }
            else
            {
                sb.AppendLine();
            }
        }

        if (pkCols.Count > 0)
        {
            sb.Append($"    CONSTRAINT \"pk_{tableName}\" PRIMARY KEY ({string.Join(", ", pkCols)})");
            if (fks.Count > 0) sb.AppendLine(",");
            else sb.AppendLine();
        }

        for (int i = 0; i < fks.Count; i++)
        {
            var fk = fks[i];
            sb.Append($"    CONSTRAINT \"{fk.ConstraintName}\" FOREIGN KEY (\"{fk.SourceColumn}\") REFERENCES \"{fk.TargetTable}\" (\"{fk.TargetColumn}\")");
            if (!string.IsNullOrEmpty(fk.OnUpdate) && fk.OnUpdate != "NO ACTION") sb.Append($" ON UPDATE {fk.OnUpdate}");
            if (!string.IsNullOrEmpty(fk.OnDelete) && fk.OnDelete != "NO ACTION") sb.Append($" ON DELETE {fk.OnDelete}");

            if (i < fks.Count - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }

        sb.AppendLine(");");

        var nonPkIndexes = indexes.Where(idx => !idx.IsPrimaryKey && !string.IsNullOrEmpty(idx.Definition)).ToList();
        if (nonPkIndexes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("-- Indexes");
            foreach (var idx in nonPkIndexes)
            {
                sb.AppendLine($"{idx.Definition};");
            }
        }

        return sb.ToString();
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        var fks = new List<ForeignKeyInfo>();
        if (_connection == null) return fks;

        string? schemaName = null;
        string? cleanTable = tableName;
        if (!string.IsNullOrEmpty(tableName) && tableName.Contains('.'))
        {
            var parts = tableName.Split('.', 2);
            schemaName = parts[0].Trim('"');
            cleanTable = parts[1].Trim('"');
        }
        else if (!string.IsNullOrEmpty(tableName))
        {
            cleanTable = tableName.Trim('"');
        }

        const string catalogSql = @"
            SELECT
                con.conname AS constraint_name,
                cl_src.relname AS source_table,
                att_src.attname AS source_column,
                cl_tgt.relname AS target_table,
                att_tgt.attname AS target_column,
                CASE con.confupdtype
                    WHEN 'a' THEN 'NO ACTION'
                    WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'
                    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT'
                    ELSE 'NO ACTION'
                END AS update_rule,
                CASE con.confdeltype
                    WHEN 'a' THEN 'NO ACTION'
                    WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'
                    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT'
                    ELSE 'NO ACTION'
                END AS delete_rule
            FROM pg_constraint con
            JOIN pg_class cl_src ON cl_src.oid = con.conrelid
            JOIN pg_namespace ns_src ON ns_src.oid = cl_src.relnamespace
            JOIN pg_class cl_tgt ON cl_tgt.oid = con.confrelid
            JOIN pg_namespace ns_tgt ON ns_tgt.oid = cl_tgt.relnamespace
            CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
            CROSS JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS fk(confnum, ford)
            JOIN pg_attribute att_src ON att_src.attrelid = con.conrelid AND att_src.attnum = k.attnum
            JOIN pg_attribute att_tgt ON att_tgt.attrelid = con.confrelid AND att_tgt.attnum = fk.confnum
            WHERE con.contype = 'f'
              AND k.ord = fk.ford
              AND (CAST(@tableName AS text) IS NULL OR cl_src.relname = @tableName OR cl_src.relname = LOWER(@tableName))
              AND (CAST(@schemaName AS text) IS NULL OR ns_src.nspname = @schemaName)
              AND ns_src.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY cl_src.relname, con.conname, k.ord;";

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = catalogSql;
            cmd.Parameters.Add(new NpgsqlParameter("tableName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)cleanTable ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("schemaName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schemaName ?? DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                fks.Add(new ForeignKeyInfo
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
        }
        catch
        {
            // Fallback to information_schema
        }

        if (fks.Count == 0 && !string.IsNullOrEmpty(cleanTable))
        {
            const string fallbackSql = @"
                SELECT
                    tc.constraint_name,
                    tc.table_name AS source_table,
                    kcu.column_name AS source_column,
                    ccu.table_name AS target_table,
                    ccu.column_name AS target_column,
                    rc.update_rule,
                    rc.delete_rule
                FROM information_schema.table_constraints AS tc
                JOIN information_schema.key_column_usage AS kcu
                  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                JOIN information_schema.referential_constraints AS rc
                  ON tc.constraint_name = rc.constraint_name AND tc.table_schema = rc.constraint_schema
                JOIN information_schema.constraint_column_usage AS ccu
                  ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND (tc.table_name = @tableName OR tc.table_name = LOWER(@tableName))
                  AND (CAST(@schemaName AS text) IS NULL OR tc.table_schema = @schemaName)
                ORDER BY tc.table_name, kcu.ordinal_position;";

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = fallbackSql;
                cmd.Parameters.Add(new NpgsqlParameter("tableName", NpgsqlTypes.NpgsqlDbType.Text) { Value = cleanTable });
                cmd.Parameters.Add(new NpgsqlParameter("schemaName", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)schemaName ?? DBNull.Value });

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    fks.Add(new ForeignKeyInfo
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
            }
            catch
            {
                // Ignore fallback error
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

        foreach (var table in tablesOnly)
        {
            var cols = await GetTableColumnsAsync(table.Name);
            tableColumns[table.Name] = cols;
            var fks = await GetForeignKeysAsync(table.Name);
            tableFks[table.Name] = fks;

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

            foreach (var fk in fks)
            {
                var card = ErdLayoutService.InferCardinality(fk, cols, fks);
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

        ErdLayoutService.ApplyTopologicalLayout(graph);

        var dbId = $"{Config.Host}_{Config.Database}";
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
        NpgsqlTransaction? tx = null;
        try
        {
            tx = await _connection.BeginTransactionAsync(cancellationToken);
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
        _sshTunnelService.Dispose();
    }
}
