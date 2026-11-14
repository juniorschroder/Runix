namespace Runix.Infrastructure.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Resilience;
using Xunit;

public class TransactionalBatchExecutionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ConnectionConfig _config;

    public TransactionalBatchExecutionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_batch_test_{Guid.NewGuid():N}.db");
        _config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _dbPath,
            Name = "BatchTestDb"
        };
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private async Task<SqliteDatabaseService> CreateAndSeedDatabaseAsync()
    {
        var service = new SqliteDatabaseService(_config);
        await service.ConnectAsync();

        await service.ExecuteQueryAsync(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                price REAL NOT NULL,
                stock INTEGER NOT NULL
            );
            INSERT INTO products (id, name, price, stock) VALUES (1, 'Laptop', 1500.0, 10);
            INSERT INTO products (id, name, price, stock) VALUES (2, 'Mouse', 25.0, 50);
        ");

        return service;
    }

    [Fact]
    public async Task ExecuteBatchTransactionalAsync_AllCommandsSucceed_AppliesAndCommitsAllChanges()
    {
        using var service = await CreateAndSeedDatabaseAsync();

        var commands = new List<ParameterizedCommand>
        {
            new("UPDATE \"products\" SET \"stock\" = @p0 WHERE \"id\" = @p1;", new() { ["@p0"] = 8, ["@p1"] = 1 }),
            new("INSERT INTO \"products\" (\"id\", \"name\", \"price\", \"stock\") VALUES (@p0, @p1, @p2, @p3);", 
                new() { ["@p0"] = 3, ["@p1"] = "Keyboard", ["@p2"] = 75.0, ["@p3"] = 20 }),
            new("DELETE FROM \"products\" WHERE \"id\" = @p0;", new() { ["@p0"] = 2 })
        };

        var result = await service.ExecuteBatchTransactionalAsync(commands);

        result.Success.Should().BeTrue();
        result.AffectedRows.Should().Be(3);

        // Verify state after commit
        var verifyQuery = await service.ExecuteQueryAsync("SELECT id, name, stock FROM products ORDER BY id;");
        verifyQuery.Rows.Should().HaveCount(2);

        // Row 1: Laptop with stock 8
        verifyQuery.Rows[0][1].Should().Be("Laptop");
        Convert.ToInt32(verifyQuery.Rows[0][2]).Should().Be(8);

        // Row 2: Keyboard (id=3)
        verifyQuery.Rows[1][1].Should().Be("Keyboard");
        Convert.ToInt32(verifyQuery.Rows[1][2]).Should().Be(20);
    }

    [Fact]
    public async Task ExecuteBatchTransactionalAsync_CommandFails_RollsBackEntireBatchWithoutPartialWrites()
    {
        using var service = await CreateAndSeedDatabaseAsync();

        var commands = new List<ParameterizedCommand>
        {
            // Valid update
            new("UPDATE \"products\" SET \"price\" = @p0 WHERE \"id\" = @p1;", new() { ["@p0"] = 9999.0, ["@p1"] = 1 }),
            // Invalid insert violating NOT NULL constraint on name
            new("INSERT INTO \"products\" (\"id\", \"name\", \"price\", \"stock\") VALUES (@p0, @p1, @p2, @p3);",
                new() { ["@p0"] = 4, ["@p1"] = DBNull.Value, ["@p2"] = 50.0, ["@p3"] = 10 })
        };

        var result = await service.ExecuteBatchTransactionalAsync(commands);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();

        // Verify that the first UPDATE was completely rolled back!
        var verifyQuery = await service.ExecuteQueryAsync("SELECT price FROM products WHERE id = 1;");
        verifyQuery.Rows.Should().HaveCount(1);
        Convert.ToDouble(verifyQuery.Rows[0][0]).Should().Be(1500.0, "price should remain 1500.0 due to atomic transaction rollback");
    }

    [Fact]
    public async Task ResilientDatabaseService_ExecuteBatchTransactionalAsync_ExecutesSuccessfullyThroughResilienceWrapper()
    {
        using var rawService = await CreateAndSeedDatabaseAsync();
        using var resilientService = new ResilientDatabaseService(rawService);

        var commands = new List<ParameterizedCommand>
        {
            new("UPDATE \"products\" SET \"stock\" = @p0 WHERE \"id\" = @p1;", new() { ["@p0"] = 5, ["@p1"] = 1 })
        };

        var result = await resilientService.ExecuteBatchTransactionalAsync(commands);

        result.Success.Should().BeTrue();
        result.AffectedRows.Should().Be(1);

        var verify = await resilientService.ExecuteQueryAsync("SELECT stock FROM products WHERE id = 1;");
        Convert.ToInt32(verify.Rows[0][0]).Should().Be(5);
    }
}
