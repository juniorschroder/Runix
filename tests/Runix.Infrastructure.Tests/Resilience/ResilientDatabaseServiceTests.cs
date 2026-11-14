using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Resilience;
using Xunit;

namespace Runix.Infrastructure.Tests.Resilience;

public class ResilientDatabaseServiceTests
{
    private class FakeDatabaseService : IDatabaseService
    {
        public ConnectionConfig Config { get; set; } = new ConnectionConfig { Type = DatabaseType.SQLite };
        public bool IsConnected { get; set; } = true;
        public CircuitState CurrentCircuitState => CircuitState.Closed;
        public event Action<ResilienceEvent>? ResilienceEventOccurred;

        public int ExecutionCount { get; private set; }
        public Func<int, Task<QueryResult>>? QueryHandler { get; set; }

        public Task<QueryResult> ExecuteQueryAsync(string sql, int limit = 50000)
        {
            ExecutionCount++;
            if (QueryHandler != null)
            {
                return QueryHandler(ExecutionCount);
            }
            return Task.FromResult(QueryResult.CreateSuccess(
                new List<string> { "val" },
                new List<List<object?>> { new() { 1 } },
                1.0));
        }

        public Task ConnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<bool> TestConnectionAsync() => Task.FromResult(true);
        public Task<List<TableInfo>> GetTablesAsync() => Task.FromResult(new List<TableInfo>());
        public Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName) => Task.FromResult(new List<ColumnInfo>());
        public Task<List<TableIndexInfo>> GetTableIndexesAsync(string tableName) => Task.FromResult(new List<TableIndexInfo>());
        public Task<string> GetTableDdlAsync(string tableName) => Task.FromResult("");
        public Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string? tableName = null) => Task.FromResult(new List<ForeignKeyInfo>());
        public Task<ErdGraphModel> GetErdGraphAsync() => Task.FromResult(new ErdGraphModel());
        public Task<QueryResult> GetTableDataAsync(string tableName, int offset = 0, int limit = 100, string? filter = null, string? sortColumn = null, bool sortAsc = true)
            => Task.FromResult(QueryResult.CreateSuccess(new List<string>(), new List<List<object?>>(), 1.0));
        public Task<QueryResult> ExecuteBatchTransactionalAsync(IReadOnlyList<ParameterizedCommand> commands, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(new QueryResult { Success = true });
        public void Dispose() { }
    }

    [Fact]
    public async Task Retry_TransientError_RetriesAndEventuallySucceeds()
    {
        var fake = new FakeDatabaseService();
        var capturedEvents = new List<ResilienceEvent>();

        // Fail first 2 attempts with SQLITE_BUSY, then succeed on 3rd attempt
        fake.QueryHandler = attempt =>
        {
            if (attempt <= 2)
            {
                throw new SqliteException("database is locked", 5);
            }
            return Task.FromResult(QueryResult.CreateSuccess(
                new List<string> { "status" },
                new List<List<object?>> { new() { "OK" } },
                2.0));
        };

        var resilient = new ResilientDatabaseService(fake);
        resilient.ResilienceEventOccurred += ev => capturedEvents.Add(ev);

        var result = await resilient.ExecuteQueryAsync("SELECT * FROM items;");

        result.Success.Should().BeTrue();
        fake.ExecutionCount.Should().Be(3);
        capturedEvents.Should().HaveCount(2);
        capturedEvents[0].Type.Should().Be(ResilienceEventType.RetryAttempt);
        capturedEvents[0].Attempt.Should().Be(1);
        capturedEvents[1].Type.Should().Be(ResilienceEventType.RetryAttempt);
        capturedEvents[1].Attempt.Should().Be(2);
    }

    [Fact]
    public async Task CircuitBreaker_ConsecutiveFailures_OpensAndFailsFast()
    {
        var fake = new FakeDatabaseService();
        var capturedEvents = new List<ResilienceEvent>();

        // Always throw transient error
        fake.QueryHandler = _ => throw new TimeoutException("Database connection timeout");

        var resilient = new ResilientDatabaseService(fake);
        resilient.ResilienceEventOccurred += ev => capturedEvents.Add(ev);

        // Execute non-idempotent mutation queries so retry is not triggered, allowing circuit breaker to count failures
        for (int i = 0; i < 6; i++)
        {
            try
            {
                await resilient.ExecuteQueryAsync("INSERT INTO items VALUES (1);");
            }
            catch (TimeoutException) { }
        }

        // Circuit should now be open
        resilient.CurrentCircuitState.Should().Be(CircuitState.Open);
        capturedEvents.Should().Contain(e => e.Type == ResilienceEventType.CircuitOpen);

        // Next call must fast-fail without calling fake.ExecutionCount again
        var countBefore = fake.ExecutionCount;
        var fastFailResult = await resilient.ExecuteQueryAsync("SELECT * FROM items;");

        fastFailResult.Success.Should().BeFalse();
        fastFailResult.ErrorMessage.Should().Contain("[Circuit Breaker Open]");
        fake.ExecutionCount.Should().Be(countBefore, "Underlying service should not be invoked when circuit is open");
    }

    [Fact]
    public async Task FastFail_DeterministicSyntaxError_FailsImmediatelyWithoutRetry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"syntax_test_{Guid.NewGuid():N}.db");
        var sqliteService = new SqliteDatabaseService(new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = dbPath
        });

        var capturedEvents = new List<ResilienceEvent>();
        var resilient = new ResilientDatabaseService(sqliteService);
        resilient.ResilienceEventOccurred += ev => capturedEvents.Add(ev);

        try
        {
            await resilient.ConnectAsync();

            // Deliberately broken SQL
            var result = await resilient.ExecuteQueryAsync("SELECT * FROM;");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            capturedEvents.Should().BeEmpty("Syntax errors are deterministic and must never trigger retry events");
        }
        finally
        {
            resilient.Dispose();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task MutationQuery_TransientError_DoesNotRetry()
    {
        var fake = new FakeDatabaseService();
        var retryEvents = new List<ResilienceEvent>();

        fake.QueryHandler = _ => throw new SqliteException("database is locked", 5);

        var resilient = new ResilientDatabaseService(fake);
        resilient.ResilienceEventOccurred += ev =>
        {
            if (ev.Type == ResilienceEventType.RetryAttempt)
                retryEvents.Add(ev);
        };

        // Mutation queries (INSERT, UPDATE, DELETE) must never retry
        try
        {
            await resilient.ExecuteQueryAsync("INSERT INTO audit_log (msg) VALUES ('Action');");
        }
        catch (SqliteException) { }

        fake.ExecutionCount.Should().Be(1, "Mutation query should only execute once and not retry");
        retryEvents.Should().BeEmpty("Mutation query should never trigger retry attempts");
    }
}
