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

    public Task<List<TableInfo>> GetTablesAsync()
    {
        // Implementação inicial funcional — será enriquecida na Fase 41
        return Task.FromResult(new List<TableInfo>());
    }

    public Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        return Task.FromResult(new List<ColumnInfo>());
    }

    public Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        return Task.FromResult(new List<TableIndexInfo>());
    }

    public Task<string> GetTableDdlAsync(string tableName)
    {
        return Task.FromResult($"-- DDL for Oracle table {tableName}\n");
    }

    public Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        return Task.FromResult(new List<ForeignKeyInfo>());
    }

    public Task<ErdGraphModel> GetErdGraphAsync()
    {
        return Task.FromResult(new ErdGraphModel());
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
