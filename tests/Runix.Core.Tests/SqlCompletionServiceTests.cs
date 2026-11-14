namespace Runix.Core.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

public class SqlCompletionServiceTests
{
    private readonly SqlCompletionService _completionService = new();

    private static (List<TableInfo> Tables, Dictionary<string, IReadOnlyList<ColumnInfo>> Columns) CreateSampleSchema()
    {
        var tables = new List<TableInfo>
        {
            new() { Name = "cheeses", Type = "table", Schema = "main" },
            new() { Name = "producers", Type = "table", Schema = "main" },
            new() { Name = "v_cheese_summary", Type = "VIEW", Schema = "main" }
        };

        var columns = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["cheeses"] = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT" },
                new() { Name = "price", DataType = "REAL" },
                new() { Name = "producer_id", DataType = "INTEGER" }
            },
            ["producers"] = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT" },
                new() { Name = "country_id", DataType = "INTEGER" }
            }
        };

        return (tables, columns);
    }

    [Fact]
    public void GetCompletions_TypingSelect_ReturnsSelectKeyword()
    {
        var (tables, columns) = CreateSampleSchema();
        var context = new SqlContextInfo
        {
            CurrentWord = "SEL",
            ActiveClause = "SELECT"
        };

        var items = _completionService.GetCompletions(context, tables, columns);

        Assert.NotEmpty(items);
        var selectItem = items.FirstOrDefault(i => i.Label == "SELECT");
        Assert.NotNull(selectItem);
        Assert.Equal(CompletionItemKind.Keyword, selectItem.Kind);
    }

    [Fact]
    public void GetCompletions_AfterFromClause_PrioritizesTablesAndViews()
    {
        var (tables, columns) = CreateSampleSchema();
        var context = new SqlContextInfo
        {
            CurrentWord = "ch",
            ActiveClause = "FROM"
        };

        var items = _completionService.GetCompletions(context, tables, columns);

        Assert.NotEmpty(items);
        var firstItem = items[0];
        Assert.Equal("cheeses", firstItem.Label);
        Assert.Equal(CompletionItemKind.Table, firstItem.Kind);
    }

    [Fact]
    public void GetCompletions_AfterAliasDot_ReturnsOnlyTargetTableColumns()
    {
        var (tables, columns) = CreateSampleSchema();
        var context = new SqlContextInfo
        {
            CurrentWord = "p",
            PrecedingIdentifier = "c",
            TableAliases = new() { ["c"] = "cheeses", ["p"] = "producers" }
        };

        var items = _completionService.GetCompletions(context, tables, columns);

        Assert.NotEmpty(items);
        // Should only return columns belonging to 'cheeses' that match 'p': 'price' and 'producer_id'
        Assert.All(items, i => Assert.Equal(CompletionItemKind.Column, i.Kind));
        Assert.Contains(items, i => i.Label == "price");
        Assert.Contains(items, i => i.Label == "producer_id");

        // Must NOT contain columns from producers (such as country_id)
        Assert.DoesNotContain(items, i => i.Label == "country_id");
    }

    [Fact]
    public void GetCompletions_TypingSnippetPrefix_ReturnsSnippet()
    {
        var (tables, columns) = CreateSampleSchema();
        var context = new SqlContextInfo
        {
            CurrentWord = "sel",
            ActiveClause = null
        };

        var items = _completionService.GetCompletions(context, tables, columns);

        var snippet = items.FirstOrDefault(i => i.Kind == CompletionItemKind.Snippet && i.Label == "sel");
        Assert.NotNull(snippet);
        Assert.Equal("SELECT * FROM ", snippet.InsertText);
    }

    [Fact]
    public void GetCompletions_Performance_CompletesUnder5Milliseconds()
    {
        // Generate mock large schema: 100 tables with 10 columns each = 1,000 columns
        var largeTables = new List<TableInfo>();
        var largeColumns = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 100; i++)
        {
            string tName = $"table_{i:D3}";
            largeTables.Add(new TableInfo { Name = tName, Schema = "public", Type = "table" });

            var colList = new List<ColumnInfo>();
            for (int c = 0; c < 10; c++)
            {
                colList.Add(new ColumnInfo { Name = $"col_{c}_{tName}", DataType = "VARCHAR" });
            }
            largeColumns[tName] = colList;
        }

        var context = new SqlContextInfo
        {
            CurrentWord = "col",
            ActiveClause = "SELECT",
            ReferencedTables = new() { "table_001", "table_002" }
        };

        // Warmup
        _completionService.GetCompletions(context, largeTables, largeColumns);

        // Benchmark
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            var results = _completionService.GetCompletions(context, largeTables, largeColumns);
            Assert.NotEmpty(results);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / 20.0;
        Assert.True(avgMs < 5.0, $"Average completion latency should be < 5ms, but was {avgMs:F2}ms");
    }
}
