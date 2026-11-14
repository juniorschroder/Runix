using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Npgsql;
using Runix.Core.Models;
using Vard.Abstractions;
using Vard.Builders;
using Vard.Policies;

namespace Runix.Infrastructure.Resilience;

public static class ResiliencePolicyFactory
{
    public const int DefaultRetryCount = 5;
    public static readonly TimeSpan PostgresInitialDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan SqliteInitialDelay = TimeSpan.FromMilliseconds(50);
    public const int DefaultCircuitBreakerThreshold = 6;
    public static readonly TimeSpan DefaultCircuitBreakerDuration = TimeSpan.FromSeconds(30);

    public static bool IsTransientException(Exception ex)
    {
        if (ex is NpgsqlException pgEx)
        {
            if (pgEx.IsTransient) return true;
            if (pgEx.SqlState != null && (pgEx.SqlState.StartsWith("08", StringComparison.Ordinal) || pgEx.SqlState == "57P01"))
            {
                return true;
            }
            return false;
        }

        if (ex is SqliteException sqlEx)
        {
            // 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED
            return sqlEx.SqliteErrorCode == 5 || sqlEx.SqliteErrorCode == 6;
        }

        if (ex is TimeoutException || ex is IOException)
        {
            return true;
        }

        return false;
    }

    public static RetryPolicy CreateRetryPolicy(
        DatabaseType dbType,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        var initialDelay = dbType == DatabaseType.SQLite ? SqliteInitialDelay : PostgresInitialDelay;

        return (RetryPolicy)VardPolicy
            .Handle<Exception>(IsTransientException)
            .RetryWithBackoff(
                retryCount: DefaultRetryCount,
                initialDelay: initialDelay,
                backoffType: BackoffType.Exponential,
                useJitter: true,
                onRetry: (ex, _, attempt, delay, _) =>
                {
                    onRetry?.Invoke(ex ?? new InvalidOperationException("Transient database fault"), attempt, delay);
                });
    }

    public static CircuitBreakerPolicy CreateCircuitBreakerPolicy(
        DatabaseType dbType,
        Action<Exception, TimeSpan>? onBreak = null,
        Action? onReset = null,
        Action? onHalfOpen = null)
    {
        return (CircuitBreakerPolicy)VardPolicy
            .Handle<Exception>(IsTransientException)
            .CircuitBreaker(
                exceptionsAllowedBeforeBreaking: DefaultCircuitBreakerThreshold,
                durationOfBreak: DefaultCircuitBreakerDuration,
                onBreak: (ex, duration, _) => onBreak?.Invoke(ex ?? new InvalidOperationException("Circuit breaker threshold exceeded"), duration),
                onReset: _ => onReset?.Invoke(),
                onHalfOpen: _ => onHalfOpen?.Invoke());
    }
}
