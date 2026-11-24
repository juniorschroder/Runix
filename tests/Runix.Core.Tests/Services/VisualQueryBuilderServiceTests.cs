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

    [Fact]
    public void BuildSql_NestedFilterTree_ProducesBalancedParenthesesAndOperators()
    {
        // WHERE ("status" = 'active' AND ("role" = 'admin' OR "points" >= 100))
        var rootGroup = new VisualFilterGroup { LogicalOperator = "AND" };
        rootGroup.AddCondition(new VisualFilterCondition
        {
            ColumnName = "status",
            ComparisonOperator = "=",
            Value = "active"
        });

        var subGroup = new VisualFilterGroup { LogicalOperator = "OR" };
        subGroup.AddCondition(new VisualFilterCondition
        {
            ColumnName = "role",
            ComparisonOperator = "=",
            Value = "admin"
        });
        subGroup.AddCondition(new VisualFilterCondition
        {
            ColumnName = "points",
            ComparisonOperator = ">=",
            Value = "100"
        });

        rootGroup.AddGroup(subGroup);

        var model = new VisualQueryModel
        {
            BaseTable = "users",
            FilterTree = rootGroup
        };

        var sql = _builder.BuildSql(model, DatabaseType.SQLite);

        Assert.Contains("\"status\" = 'active'", sql);
        Assert.Contains("(\"role\" = 'admin'", sql);
        Assert.Contains("\"points\" >= 100)", sql);
        Assert.Contains("AND", sql);
        Assert.Contains("OR", sql);
    }

    [Fact]
    public void BuildSql_NegatedFilterGroup_ProducesNotClause()
    {
        // WHERE NOT ("status" IN ('banned', 'deleted'))
        var rootGroup = new VisualFilterGroup { LogicalOperator = "AND", IsNegated = true };
        rootGroup.AddCondition(new VisualFilterCondition
        {
            ColumnName = "status",
            ComparisonOperator = "IN",
            Value = "'banned', 'deleted'"
        });

        var model = new VisualQueryModel
        {
            BaseTable = "users",
            FilterTree = rootGroup
        };

        var sql = _builder.BuildSql(model, DatabaseType.SQLite);

        Assert.Contains("WHERE NOT (\"status\" IN ('banned', 'deleted'))", sql);
    }

    [Fact]
    public void BuildSql_BetweenAndLikeShortcuts_FormatCorrectly()
    {
        var root = new VisualFilterGroup();
        root.AddCondition(new VisualFilterCondition
        {
            ColumnName = "price",
            ComparisonOperator = "BETWEEN",
            Value = "10",
            SecondValue = "50"
        });
        root.AddCondition(new VisualFilterCondition
        {
            ColumnName = "name",
            ComparisonOperator = "CONTAINS",
            Value = "Pro"
        });
        root.AddCondition(new VisualFilterCondition
        {
            ColumnName = "sku",
            ComparisonOperator = "STARTS WITH",
            Value = "SKU-"
        });

        var model = new VisualQueryModel
        {
            BaseTable = "products",
            FilterTree = root
        };

        var sql = _builder.BuildSql(model, DatabaseType.SQLite);

        Assert.Contains("\"price\" BETWEEN 10", sql);
        Assert.Contains("50", sql);
        Assert.Contains("\"name\" LIKE '%Pro%'", sql);
        Assert.Contains("\"sku\" LIKE 'SKU-%'", sql);
    }

    [Fact]
    public void BuildSql_DialectQuoting_SqlServer_UsesSquareBrackets()
    {
        var model = new VisualQueryModel
        {
            BaseTable = "Customers",
            BaseTableAlias = "c",
            SelectColumns = new List<VisualSelectColumn>
            {
                new() { TableAlias = "c", ColumnName = "FirstName", OutputAlias = "Nome" }
            }
        };

        var sql = _builder.BuildSql(model, DatabaseType.SqlServer);

        Assert.Contains("SELECT\n    c.[FirstName] AS [Nome]", sql);
        Assert.Contains("FROM [Customers] c", sql);
    }

    [Fact]
    public void BuildSql_DialectQuoting_MySql_UsesBackticks()
    {
        var model = new VisualQueryModel
        {
            BaseTable = "orders",
            BaseTableAlias = "o",
            SelectColumns = new List<VisualSelectColumn>
            {
                new() { TableAlias = "o", ColumnName = "order_date", OutputAlias = "Data" }
            }
        };

        var sql = _builder.BuildSql(model, DatabaseType.MySQL);

        Assert.Contains("SELECT\n    o.`order_date` AS `Data`", sql);
        Assert.Contains("FROM `orders` o", sql);
    }

    [Fact]
    public void BuildSql_Pagination_SqlServer_UsesOffsetFetchAndSelectNullFallback()
    {
        var model = new VisualQueryModel
        {
            BaseTable = "users",
            Limit = 25,
            Offset = 50
        };

        // When no ORDER BY is specified, SQL Server requires an ORDER BY for OFFSET/FETCH
        var sql = _builder.BuildSql(model, DatabaseType.SqlServer);

        Assert.Contains("ORDER BY\n    (SELECT NULL)", sql);
        Assert.Contains("OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY;", sql);
    }

    [Fact]
    public void BuildSql_HavingClause_GeneratesAggregateFilters()
    {
        var havingGroup = new VisualFilterGroup();
        havingGroup.AddCondition(new VisualFilterCondition
        {
            ColumnName = "COUNT(*)",
            ComparisonOperator = ">",
            Value = "5"
        });

        var model = new VisualQueryModel
        {
            BaseTable = "orders",
            SelectColumns = new List<VisualSelectColumn>
            {
                new() { ColumnName = "customer_id" },
                new() { ColumnName = "id", AggregateFunction = "COUNT", OutputAlias = "order_count" }
            },
            HavingTree = havingGroup
        };

        var sql = _builder.BuildSql(model, DatabaseType.PostgreSQL);

        Assert.Contains("GROUP BY\n    \"customer_id\"", sql);
        Assert.Contains("HAVING \"COUNT(*)\" > 5", sql);
    }

    [Fact]
    public void SuggestJoinOptions_MultipleForeignKeys_ReturnsAllCandidates()
    {
        var fks = new List<ForeignKeyInfo>
        {
            new()
            {
                SourceTable = "tasks",
                SourceColumn = "creator_id",
                TargetTable = "users",
                TargetColumn = "id"
            },
            new()
            {
                SourceTable = "tasks",
                SourceColumn = "assignee_id",
                TargetTable = "users",
                TargetColumn = "id"
            }
        };

        var options = _builder.SuggestJoinOptions("tasks", "t", "users", "u", fks);

        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.SourceColumn == "creator_id" && o.TargetColumn == "id");
        Assert.Contains(options, o => o.SourceColumn == "assignee_id" && o.TargetColumn == "id");
    }

    [Fact]
    public void BuildSql_FullOuterJoinAndCrossJoin_GeneratesAppropriateClauses()
    {
        var model = new VisualQueryModel
        {
            BaseTable = "dept",
            Joins = new List<VisualJoinClause>
            {
                new()
                {
                    JoinType = "FULL OUTER JOIN",
                    TargetTable = "emp",
                    SourceColumn = "id",
                    TargetColumn = "dept_id"
                },
                new()
                {
                    JoinType = "CROSS JOIN",
                    TargetTable = "calendar"
                }
            }
        };

        var sql = _builder.BuildSql(model, DatabaseType.PostgreSQL);

        Assert.Contains("FULL OUTER JOIN \"emp\"", sql);
        Assert.Contains("ON dept.\"id\" = emp.\"dept_id\"", sql);
        Assert.Contains("CROSS JOIN \"calendar\"", sql);
    }
}
