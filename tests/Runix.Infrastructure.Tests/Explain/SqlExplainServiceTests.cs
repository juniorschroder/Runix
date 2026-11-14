using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Explain;
using Xunit;

namespace Runix.Infrastructure.Tests.Explain;

public class SqlExplainServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqlExplainService _explainService;

    public SqlExplainServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_explain_test_{Guid.NewGuid():N}.db");
        _explainService = SqlExplainService.Instance;
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public void ParsePostgresPlan_WithComplexMultiNodePlan_ExtractsMetricsAndCalculatesCostPercentages()
    {
        var json = @"[
          {
            ""Plan"": {
              ""Node Type"": ""Aggregate"",
              ""Startup Cost"": 120.50,
              ""Total Cost"": 125.00,
              ""Plan Rows"": 1,
              ""Plan Width"": 8,
              ""Actual Startup Time"": 1.150,
              ""Actual Total Time"": 1.155,
              ""Actual Rows"": 1,
              ""Actual Loops"": 1,
              ""Plans"": [
                {
                  ""Node Type"": ""Hash Join"",
                  ""Startup Cost"": 20.00,
                  ""Total Cost"": 115.00,
                  ""Plan Rows"": 250,
                  ""Plan Width"": 64,
                  ""Actual Startup Time"": 0.450,
                  ""Actual Total Time"": 0.980,
                  ""Actual Rows"": 200,
                  ""Actual Loops"": 1,
                  ""Hash Cond"": ""(c.region_id = r.id)"",
                  ""Plans"": [
                    {
                      ""Node Type"": ""Seq Scan"",
                      ""Relation Name"": ""cheeses"",
                      ""Alias"": ""c"",
                      ""Startup Cost"": 0.00,
                      ""Total Cost"": 85.00,
                      ""Plan Rows"": 300,
                      ""Plan Width"": 32,
                      ""Actual Startup Time"": 0.020,
                      ""Actual Total Time"": 0.420,
                      ""Actual Rows"": 200,
                      ""Actual Loops"": 1,
                      ""Filter"": ""(price > 50.0)"",
                      ""Rows Removed by Filter"": 150,
                      ""Shared Hit Blocks"": 14,
                      ""Shared Read Blocks"": 2
                    },
                    {
                      ""Node Type"": ""Hash"",
                      ""Startup Cost"": 15.00,
                      ""Total Cost"": 15.00,
                      ""Plan Rows"": 50,
                      ""Plan Width"": 36,
                      ""Actual Startup Time"": 0.120,
                      ""Actual Total Time"": 0.120,
                      ""Actual Rows"": 50,
                      ""Actual Loops"": 1,
                      ""Plans"": [
                        {
                          ""Node Type"": ""Seq Scan"",
                          ""Relation Name"": ""regions"",
                          ""Alias"": ""r"",
                          ""Startup Cost"": 0.00,
                          ""Total Cost"": 15.00,
                          ""Plan Rows"": 50,
                          ""Plan Width"": 36,
                          ""Actual Startup Time"": 0.010,
                          ""Actual Total Time"": 0.080,
                          ""Actual Rows"": 50,
                          ""Actual Loops"": 1
                        }
                      ]
                    }
                  ]
                }
              ]
            },
            ""Planning Time"": 0.145,
            ""Execution Time"": 1.250
          }
        ]";

        var model = _explainService.ParsePostgresPlan(json);

        model.Dialect.Should().Be("PostgreSQL");
        model.HasAnalyzeData.Should().BeTrue();
        model.PlanningTimeMs.Should().Be(0.145);
        model.ExecutionTimeMs.Should().Be(1.250);
        model.TotalCost.Should().Be(125.00);

        var root = model.RootNode;
        root.Should().NotBeNull();
        root!.NodeType.Should().Be("Aggregate");
        root.CostPercent.Should().Be(100.0);
        root.Children.Should().HaveCount(1);

        var hashJoin = root.Children[0];
        hashJoin.NodeType.Should().Be("Hash Join");
        hashJoin.Children.Should().HaveCount(2);

        var seqScan = hashJoin.Children[0];
        seqScan.NodeType.Should().Be("Seq Scan");
        seqScan.RelationName.Should().Be("cheeses");
        seqScan.TotalCost.Should().Be(85.00);
        seqScan.CostPercent.Should().BeApproximately(68.0, 0.5); // 85 / 125 * 100 = 68%
        seqScan.IsBottleneck.Should().BeTrue();
        seqScan.FilterCondition.Should().Be("(price > 50.0)");
        seqScan.RowsRemovedByFilter.Should().Be(150);
        seqScan.SharedHitBlocks.Should().Be(14);
        seqScan.SharedReadBlocks.Should().Be(2);
    }

    [Fact]
    public void ParseSqlitePlan_BuildsHierarchyAndFlagsScanTableAsBottleneck()
    {
        var result = new QueryResult
        {
            Success = true,
            Columns = new List<string> { "id", "parent", "notused", "detail" },
            Rows = new List<List<object?>>
            {
                new List<object?> { 2, 0, 0, "SCAN cheeses" },
                new List<object?> { 4, 0, 0, "SEARCH regions USING INDEX idx_regions_id (id=?)" },
                new List<object?> { 6, 0, 0, "USE TEMP B-TREE FOR ORDER BY" }
            }
        };

        var model = _explainService.ParseSqlitePlan(result);

        model.Dialect.Should().Be("SQLite");
        model.RootNode.Should().NotBeNull();

        var nodes = ExecutionPlanModel.FlattenNodes(model.RootNode).ToList();
        nodes.Should().Contain(n => n.RelationName == "cheeses" && n.IsBottleneck && n.NodeType.Contains("SCAN"));
        nodes.Should().Contain(n => n.RelationName == "regions" && n.IndexName == "idx_regions_id" && n.OperatorCategory == "Index Scan");
        nodes.Should().Contain(n => n.NodeType.Contains("TEMP B-TREE") && n.OperatorCategory == "Sort");
    }

    [Fact]
    public void AnalyzePlan_GeneratesIndexRecommendation_ForFilteredSeqScan()
    {
        var plan = new ExecutionPlanModel
        {
            Dialect = "PostgreSQL",
            TotalCost = 100.0,
            RootNode = new ExplainNode
            {
                NodeType = "Seq Scan",
                RelationName = "orders",
                FilterCondition = "(customer_id = 42) AND (order_date >= '2026-01-01')",
                RowsRemovedByFilter = 500,
                TotalCost = 100.0,
                CostPercent = 100.0,
                IsBottleneck = true
            }
        };

        var recommendations = IndexRecommendationEngine.AnalyzePlan(plan, "SELECT * FROM orders WHERE customer_id = 42 AND order_date >= '2026-01-01'");

        recommendations.Should().NotBeEmpty();
        var rec = recommendations[0];
        rec.TableName.Should().Be("orders");
        rec.Columns.Should().Contain("customer_id");
        rec.CreateIndexSql.Should().StartWith("CREATE INDEX idx_orders_");
        rec.CreateIndexSql.Should().Contain("ON orders (");
        rec.Reason.Should().Contain("sofreu varredura completa");
    }

    [Fact]
    public async Task ExplainQueryAsync_WithRealSqliteDatabase_ExecutesAndReturnsPlan()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath,
            Name = "TestExplainDb"
        };

        using var dbService = new SqliteDatabaseService(config);
        await dbService.ConnectAsync();

        // Create table and populate rows
        await dbService.ExecuteQueryAsync(@"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                email TEXT,
                status TEXT
            );
            INSERT INTO customers (email, status) VALUES ('alice@example.com', 'active');
            INSERT INTO customers (email, status) VALUES ('bob@example.com', 'inactive');
        ");

        var sql = "SELECT * FROM customers WHERE email = 'alice@example.com';";
        var plan = await _explainService.ExplainQueryAsync(dbService, sql);

        plan.Should().NotBeNull();
        plan.Dialect.Should().Be("SQLite");
        plan.RootNode.Should().NotBeNull();

        // Since email is not indexed, it should recommend an index on email
        plan.Recommendations.Should().NotBeEmpty();
        plan.Recommendations[0].TableName.Should().Be("customers");
        plan.Recommendations[0].Columns.Should().Contain("email");
        plan.Recommendations[0].CreateIndexSql.Should().Contain("CREATE INDEX");
    }
}
