using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Resilience;

namespace Runix.Core.Interfaces;

public interface IDatabaseService : IDisposable
{
    ConnectionConfig Config { get; }
    bool IsConnected { get; }
    CircuitState CurrentCircuitState { get; }

    event Action<ResilienceEvent>? ResilienceEventOccurred;

    Task<bool> TestConnectionAsync();
    Task ConnectAsync();
    Task DisconnectAsync();

    Task<List<TableInfo>> GetTablesAsync();
    Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000);
    Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000);
    Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true);
    Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName);
    Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName);
    Task<string> GetTableDdlAsync(string tableName);

    Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null);
    Task<ErdGraphModel> GetErdGraphAsync();

    Task<QueryResult> ExecuteBatchTransactionalAsync(IReadOnlyList<ParameterizedCommand> commands, System.Threading.CancellationToken cancellationToken = default);
}
