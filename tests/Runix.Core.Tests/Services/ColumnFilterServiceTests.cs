using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class ColumnFilterServiceTests
{
    private readonly ColumnFilterService _service = new();

    [Theory]
    [InlineData("age", "> 100", "\"age\" > 100")]
    [InlineData("age", ">= 18", "\"age\" >= 18")]
    [InlineData("price", "< 50.5", "\"price\" < 50.5")]
    [InlineData("price", "<= 200", "\"price\" <= 200")]
    [InlineData("status", "!= 'active'", "\"status\" <> 'active'")]
    [InlineData("status", "<> pending", "\"status\" <> 'pending'")]
    [InlineData("code", "= 42", "\"code\" = 42")]
    [InlineData("deleted_at", "IS NULL", "\"deleted_at\" IS NULL")]
    [InlineData("deleted_at", "null", "\"deleted_at\" IS NULL")]
    [InlineData("name", "IS NOT NULL", "\"name\" IS NOT NULL")]
    [InlineData("email", "LIKE '%@gmail.com'", "\"email\" LIKE '%@gmail.com'")]
    [InlineData("title", "ILIKE '%runix%'", "\"title\" ILIKE '%runix%'")]
    [InlineData("role", "IN ('admin', 'editor')", "\"role\" IN ('admin', 'editor')")]
    [InlineData("score", "BETWEEN 10 AND 50", "\"score\" BETWEEN 10 AND 50")]
    [InlineData("id", "123", "\"id\" = 123")]
    [InlineData("name", "Maria", "\"name\" LIKE '%Maria%'")]
    [InlineData("name", "O'Connor", "\"name\" LIKE '%O''Connor%'")]
    public void BuildPredicate_ParsesInlineFilterSyntaxCorrectly(string column, string input, string expected)
    {
        var predicate = _service.BuildPredicate(column, input);
        predicate.Should().Be(expected);
    }

    [Fact]
    public void BuildWhereClause_WithMultipleColumns_CombinesWithAnd()
    {
        var filters = new Dictionary<string, string>
        {
            ["age"] = ">= 18",
            ["name"] = "Alice",
            ["deleted_at"] = "IS NULL"
        };

        var where = _service.BuildWhereClause(filters);

        where.Should().Be("(\"age\" >= 18) AND (\"name\" LIKE '%Alice%') AND (\"deleted_at\" IS NULL)");
    }

    [Fact]
    public void BuildOrderByClause_WithMultipleColumns_GeneratesMultiColumnSql()
    {
        var sorts = new List<(string Column, bool Ascending)>
        {
            ("status", true),
            ("created_at", false),
            ("priority", true)
        };

        var orderClause = _service.BuildOrderByClause(sorts);

        orderClause.Should().Be("\"status\" ASC, \"created_at\" DESC, \"priority\" ASC");
    }

    [Theory]
    [InlineData(42, "> 40", true)]
    [InlineData(42, "> 50", false)]
    [InlineData(42, "= 42", true)]
    [InlineData(42, "!= 42", false)]
    [InlineData(null, "IS NULL", true)]
    [InlineData("hello", "IS NULL", false)]
    [InlineData("hello", "IS NOT NULL", true)]
    [InlineData("Gouda Cheese", "LIKE '%gouda%'", true)]
    [InlineData("Gouda Cheese", "cheddar", false)]
    [InlineData("Gouda Cheese", "gouda", true)]
    public void MatchesValue_EvaluatesInMemoryFilterExpressions(object? cellValue, string rawInput, bool expected)
    {
        var matches = _service.MatchesValue(cellValue, rawInput);
        matches.Should().Be(expected);
    }
}
