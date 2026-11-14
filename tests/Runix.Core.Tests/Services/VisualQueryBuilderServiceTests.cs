using System.Collections.Generic;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class VisualQueryBuilderServiceTests
{
    private readonly VisualQueryBuilderService _builder = new();

    [Fact]
    public void BuildSql_GeneratesSelectJoinFilterGroupByAndOrderBy()
    {
        var model = new VisualQueryModel
        {
            BaseTable = "customers",
            BaseTableAlias = "c",
            Distinct = false,
            Limit = 50,
            SelectColumns = new List<VisualSelectColumn>
            {
                new() { IsSelected = true, TableAlias = "c", ColumnName = "country" },
                new() { IsSelected = true, TableAlias = "o", ColumnName = "total", AggregateFunction = "SUM", OutputAlias = "revenue" }
            },
            Joins = new List<VisualJoinClause>
            {
                new()
                {
                    JoinType = "INNER JOIN",
                    TargetTable = "orders",
                    TargetAlias = "o",
                    SourceTableAlias = "c",
                    SourceColumn = "id",
                    Operator = "=",
                    TargetColumn = "customer_id"
                }
            },
            Filters = new List<VisualFilterCondition>
            {
                new()
                {
                    LogicalOperator = "AND",
                    TableAlias = "c",
                    ColumnName = "active",
                    ComparisonOperator = "=",
                    Value = "1"
                },
                new()
                {
                    LogicalOperator = "AND",
                    TableAlias = "c",
                    ColumnName = "name",
                    ComparisonOperator = "LIKE",
                    Value = "%Cheese%"
                }
            },
            Sorts = new List<VisualSortClause>
            {
                new()
                {
                    TableAlias = "c",
                    ColumnName = "country",
                    Direction = "ASC"
                }
            }
        };

        var sql = _builder.BuildSql(model, DatabaseType.SQLite);

        Assert.Contains("SELECT\n    c.\"country\",\n    SUM(o.\"total\") AS \"revenue\"", sql);
        Assert.Contains("FROM \"customers\" c", sql);
        Assert.Contains("INNER JOIN \"orders\" o\n    ON c.\"id\" = o.\"customer_id\"", sql);
        Assert.Contains("WHERE c.\"active\" = 1\n    AND c.\"name\" LIKE '%Cheese%'", sql);
        Assert.Contains("GROUP BY\n    c.\"country\"", sql);
        Assert.Contains("ORDER BY\n    c.\"country\" ASC", sql);
        Assert.Contains("LIMIT 50;", sql);
    }

    [Fact]
    public void SuggestJoinClause_InfersJoinFromForeignKeyBothDirections()
    {
        var fks = new List<ForeignKeyInfo>
        {
            new()
            {
                SourceTable = "orders",
                SourceColumn = "customer_id",
                TargetTable = "customers",
                TargetColumn = "id"
            }
        };

        // Base = customers (c), Join = orders (o)
        var join1 = _builder.SuggestJoinClause("customers", "c", "orders", "o", fks);
        Assert.Equal("id", join1.SourceColumn);
        Assert.Equal("customer_id", join1.TargetColumn);

        // Base = orders (o), Join = customers (c)
        var join2 = _builder.SuggestJoinClause("orders", "o", "customers", "c", fks);
        Assert.Equal("customer_id", join2.SourceColumn);
        Assert.Equal("id", join2.TargetColumn);
    }
}
