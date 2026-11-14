using System;
using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class QueryResultTests
{
    [Fact]
    public void CreateSuccess_ValidParameters_CreatesSuccessResult()
    {
        var columns = new List<string> { "id", "name", "price" };
        var rows = new List<List<object?>>
        {
            new() { 1, "Cheddar", 12.5 },
            new() { 2, "Brie", 18.0 }
        };

        var result = QueryResult.CreateSuccess(columns, rows, elapsedMs: 4.25, affected: 2);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Columns.Should().BeEquivalentTo(columns);
        result.Rows.Should().HaveCount(2);
        result.ElapsedMilliseconds.Should().Be(4.25);
        result.AffectedRows.Should().Be(2);
    }

    [Fact]
    public void CreateError_WithErrorString_CreatesFailureResult()
    {
        var result = QueryResult.CreateError("Table 'cheeses' not found", elapsedMs: 1.5);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Table 'cheeses' not found");
        result.Columns.Should().BeEmpty();
        result.Rows.Should().BeEmpty();
        result.ElapsedMilliseconds.Should().Be(1.5);
    }

    [Fact]
    public void QueryHistoryItem_CalculatesSummaryAndDisplaySummaryCorrectly()
    {
        var timestamp = new DateTime(2026, 11, 12, 14, 30, 15);
        var item = new QueryHistoryItem
        {
            ExecutedAt = timestamp,
            Sql = "SELECT * FROM cheeses LIMIT 10;\n",
            Success = true,
            RowCount = 10,
            DurationMs = 12.3
        };

        item.FormattedTime.Should().Be("14:30:15");
        item.Summary.Should().Be("14:30:15 - 10 rows (12ms)");
        item.DisplaySummary.Should().Contain("[14:30:15]");
        item.DisplaySummary.Should().Contain("10 rows");
        item.DisplaySummary.Should().Contain("12.3ms");
        item.DisplaySummary.Should().NotContain("\n");
    }

    [Fact]
    public void QueryHistoryItem_FailedQuery_ReflectsFailureInSummary()
    {
        var timestamp = new DateTime(2026, 11, 12, 14, 30, 15);
        var item = new QueryHistoryItem
        {
            ExecutedAt = timestamp,
            Sql = "SELECT * FROM invalid;",
            Success = false,
            RowCount = 0,
            DurationMs = 5.0
        };

        item.Summary.Should().Be("14:30:15 - Failed (5ms)");
    }
}
