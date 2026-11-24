using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Vard.Abstractions;
using Vard.Builders;
using Vard.Policies;

namespace Runix.Infrastructure.Resilience;

public class ResilientDatabaseService : IDatabaseService
{
    private readonly IDatabaseService _inner;
    private readonly RetryPolicy _retryPolicy;
    private readonly CircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly PolicyWrap _idempotentPipeline;
    private readonly ISqlSafetyService _safetyService = new Runix.Core.Services.SqlSafetyService();

    public ConnectionConfig Config => _inner.Config;
    public bool IsConnected => _inner.IsConnected;

    public Runix.Core.Resilience.CircuitState CurrentCircuitState =>
        MapCircuitState(_circuitBreakerPolicy.CircuitState);

    public event Action<ResilienceEvent>? ResilienceEventOccurred;

    public ResilientDatabaseService(IDatabaseService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        var target = _inner.Config.Type == DatabaseType.SQLite
            ? _inner.Config.FilePath
            : $"{_inner.Config.Database}@{_inner.Config.Host}";

        _retryPolicy = ResiliencePolicyFactory.CreateRetryPolicy(
            _inner.Config.Type,
            onRetry: (ex, attempt, delay) =>
            {
                ResilienceEventOccurred?.Invoke(new ResilienceEvent(
                    ResilienceEventType.RetryAttempt,
                    target,
                    Attempt: attempt,
                    MaxAttempts: ResiliencePolicyFactory.DefaultRetryCount,
                    Delay: delay,
                    Reason: ex.Message));
            });

        _circuitBreakerPolicy = ResiliencePolicyFactory.CreateCircuitBreakerPolicy(
            _inner.Config.Type,
            onBreak: (ex, duration) =>
            {
                ResilienceEventOccurred?.Invoke(new ResilienceEvent(
                    ResilienceEventType.CircuitOpen,
                    target,
                    Delay: duration,
                    Reason: ex.Message));
            },
            onReset: () =>
            {
                ResilienceEventOccurred?.Invoke(new ResilienceEvent(
                    ResilienceEventType.CircuitClosed,
                    target,
                    Reason: "Circuit closed, normal operation restored."));
            },
            onHalfOpen: () =>
            {
                ResilienceEventOccurred?.Invoke(new ResilienceEvent(
                    ResilienceEventType.CircuitHalfOpen,
                    target,
                    Reason: "Circuit half-open, sending pilot request..."));
            });

        _idempotentPipeline = new PolicyWrap(_retryPolicy, _circuitBreakerPolicy);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            return await _idempotentPipeline.ExecuteAsync(
                async ct => await _inner.TestConnectionAsync(),
                CancellationToken.None);
        }
        catch (CircuitBreakerOpenException)
        {
            return false;
        }
    }

    public async Task<ConnectionDiagnosticsResult> TestConnectionDiagnosticsAsync()
    {
        try
        {
            return await _idempotentPipeline.ExecuteAsync(
                async ct => await _inner.TestConnectionDiagnosticsAsync(),
                CancellationToken.None);
        }
        catch (CircuitBreakerOpenException ex)
        {
            return new ConnectionDiagnosticsResult(
                Success: false,
                ErrorMessage: $"Circuit breaker open: {ex.Message}"
            );
        }
    }

    public async Task ConnectAsync()
    {
        await _idempotentPipeline.ExecuteAsync(
            async ct =>
            {
                await _inner.ConnectAsync();
                return true;
            },
            CancellationToken.None);
    }

    public Task DisconnectAsync()
    {
        return _inner.DisconnectAsync();
    }

    public async Task<List<TableInfo>> GetTablesAsync()
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetTablesAsync(),
            CancellationToken.None);
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName)
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetTableColumnsAsync(tableName),
            CancellationToken.None);
    }

    public async Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName)
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetTableIndexesAsync(tableName),
            CancellationToken.None);
    }

    public async Task<string> GetTableDdlAsync(string tableName)
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetTableDdlAsync(tableName),
            CancellationToken.None);
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null)
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetForeignKeysAsync(tableName),
            CancellationToken.None);
    }

    public async Task<ErdGraphModel> GetErdGraphAsync()
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetErdGraphAsync(),
            CancellationToken.None);
    }

    public async Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true)
    {
        return await _idempotentPipeline.ExecuteAsync(
            async ct => await _inner.GetTableDataAsync(tableName, offset, limit, filter, sortColumn, sortAsc),
            CancellationToken.None);
    }

    public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        // Safety & Read-Only / Production Validation Safeguard
        var safety = _safetyService.ValidateQuery(sql, Config);
        if (!safety.IsAllowed)
        {
            var errMessage = $"{safety.Reason}";
            if (!string.IsNullOrWhiteSpace(safety.OffendingStatement))
            {
                errMessage += $"\n[Comando]: {safety.OffendingStatement}";
            }
            return new List<QueryResult> { QueryResult.CreateError(errMessage) };
        }

        try
        {
            if (IsIdempotentQuery(sql))
            {
                return await _idempotentPipeline.ExecuteAsync(
                    async ct => await _inner.ExecuteMultipleQueriesAsync(sql, limit, ct),
                    cancellationToken);
            }
            else
            {
                return await _circuitBreakerPolicy.ExecuteAsync(
                    async ct => await _inner.ExecuteMultipleQueriesAsync(sql, limit, ct),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return new List<QueryResult>
            {
                new()
                {
                    Success = false,
                    ErrorMessage = "Consulta cancelada pelo usuário."
                }
            };
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            return new List<QueryResult>
            {
                QueryResult.CreateError(
                    $"[Circuit Breaker Open] Database target is currently unreachable. Failing fast without blocking. Reset in {cbEx.RetryAfter?.TotalSeconds ?? 30:F0}s.")
            };
        }
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000, CancellationToken cancellationToken = default)
    {
        // Safety & Read-Only / Production Validation Safeguard
        var safety = _safetyService.ValidateQuery(sql, Config);
        if (!safety.IsAllowed)
        {
            var errMessage = $"{safety.Reason}";
            if (!string.IsNullOrWhiteSpace(safety.OffendingStatement))
            {
                errMessage += $"\n[Comando]: {safety.OffendingStatement}";
            }
            return QueryResult.CreateError(errMessage);
        }

        try
        {
            if (IsIdempotentQuery(sql))
            {
                // Idempotent queries (SELECT, introspections) receive full Retry + Circuit Breaker
                return await _idempotentPipeline.ExecuteAsync(
                    async ct => await _inner.ExecuteQueryAsync(sql, limit, ct),
                    cancellationToken);
            }
            else
            {
                // Mutation commands (INSERT, UPDATE, DELETE, DDL) receive Circuit Breaker ONLY, never automatic Retry
                return await _circuitBreakerPolicy.ExecuteAsync(
                    async ct => await _inner.ExecuteQueryAsync(sql, limit, ct),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = "Consulta cancelada pelo usuário."
            };
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            return QueryResult.CreateError(
                $"[Circuit Breaker Open] Database target is currently unreachable. Failing fast without blocking. Reset in {cbEx.RetryAfter?.TotalSeconds ?? 30:F0}s.");
        }
    }

    public async Task<QueryResult> ExecuteBatchTransactionalAsync(
        IReadOnlyList<ParameterizedCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (Config.IsReadOnly)
        {
            return QueryResult.CreateError(
                "🔒 Conexão bloqueada em Modo Somente-Leitura (Read-Only Connection Lock). Modificações de dados em lote estão desabilitadas.");
        }

        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(
                async ct => await _inner.ExecuteBatchTransactionalAsync(commands, ct),
                cancellationToken);
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            return QueryResult.CreateError(
                $"[Circuit Breaker Open] Database target is currently unreachable. Failing fast without blocking. Reset in {cbEx.RetryAfter?.TotalSeconds ?? 30:F0}s.");
        }
    }

    public static bool IsIdempotentQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return true;
        var trimmed = sql.TrimStart();
        while (trimmed.StartsWith("--", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                var idx = trimmed.IndexOf('\n');
                if (idx < 0) return true;
                trimmed = trimmed[(idx + 1)..].TrimStart();
            }
            else if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                var idx = trimmed.IndexOf("*/", StringComparison.Ordinal);
                if (idx < 0) return true;
                trimmed = trimmed[(idx + 2)..].TrimStart();
            }
        }

        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase);
    }

    private static Runix.Core.Resilience.CircuitState MapCircuitState(Vard.Abstractions.CircuitState state) => state switch
    {
        Vard.Abstractions.CircuitState.Open => Runix.Core.Resilience.CircuitState.Open,
        Vard.Abstractions.CircuitState.HalfOpen => Runix.Core.Resilience.CircuitState.HalfOpen,
        _ => Runix.Core.Resilience.CircuitState.Closed
    };

    public void Dispose()
    {
        _inner.Dispose();
    }
}
