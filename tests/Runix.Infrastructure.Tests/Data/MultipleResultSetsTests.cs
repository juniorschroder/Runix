using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class MultipleResultSetsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseService _service;

    public MultipleResultSetsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_multi_rs_{Guid.NewGuid():N}.db");
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _dbPath
        };
        _service = new SqliteDatabaseService(config);
        _service.ConnectAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _service.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteMultipleQueriesAsync_WithTwoSelects_ReturnsTwoDistinctResultSets()
    {
        var sql = "SELECT 10 AS first_id, 'apple' AS fruit; SELECT 20 AS second_id, 'banana' AS fruit, 9.99 AS price;";

        var results = await _service.ExecuteMultipleQueriesAsync(sql);

        results.Should().HaveCount(2);

        // Result 1
        results[0].Success.Should().BeTrue();
        results[0].Columns.Should().Equal("first_id", "fruit");
        results[0].Rows.Should().HaveCount(1);
        results[0].Rows[0][0].Should().Be(10L);
        results[0].Rows[0][1].Should().Be("apple");

        // Result 2
        results[1].Success.Should().BeTrue();
        results[1].Columns.Should().Equal("second_id", "fruit", "price");
        results[1].Rows.Should().HaveCount(1);
        results[1].Rows[0][0].Should().Be(20L);
        results[1].Rows[0][1].Should().Be("banana");
    }

    [Fact]
    public async Task ExecuteQueryAsync_WithMultipleSelects_PopulatesSubResultsOnPrimary()
    {
        var sql = "SELECT 1 AS a; SELECT 2 AS b; SELECT 3 AS c;";

        var primary = await _service.ExecuteQueryAsync(sql);

        primary.Success.Should().BeTrue();
        primary.HasMultipleResults.Should().BeTrue();
        primary.SubResults.Should().HaveCount(3);
        primary.SubResults[0].Columns.Should().Equal("a");
        primary.SubResults[1].Columns.Should().Equal("b");
        primary.SubResults[2].Columns.Should().Equal("c");
    }

    [Fact]
    public async Task ExecuteMultipleQueriesAsync_WithSingleSelect_ReturnsSingleItem()
    {
        var sql = "SELECT 'single' AS val;";

        var results = await _service.ExecuteMultipleQueriesAsync(sql);

        results.Should().HaveCount(1);
        results[0].Success.Should().BeTrue();
        results[0].Columns.Should().Equal("val");
    }
}
