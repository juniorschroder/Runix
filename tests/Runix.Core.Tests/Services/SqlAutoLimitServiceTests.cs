namespace Runix.Core.Tests.Services;

using FluentAssertions;
using Runix.Core.Services;
using Xunit;

public class SqlAutoLimitServiceTests
{
    private readonly SqlAutoLimitService _service = SqlAutoLimitService.Instance;

    [Theory]
    [InlineData("SELECT * FROM users", 1000, "SELECT * FROM users LIMIT 1000", true)]
    [InlineData("SELECT * FROM users;", 1000, "SELECT * FROM users LIMIT 1000;", true)]
    [InlineData("select id, name from customers where active = 1", 500, "select id, name from customers where active = 1 LIMIT 500", true)]
    [InlineData("select id, name from customers where active = 1;", 500, "select id, name from customers where active = 1 LIMIT 500;", true)]
    [InlineData("WITH recent AS (SELECT * FROM orders) SELECT * FROM recent", 100, "WITH recent AS (SELECT * FROM orders) SELECT * FROM recent LIMIT 100", true)]
    public void ApplyAutoLimit_SelectWithoutLimit_AppendsLimit(string input, int limit, string expectedSql, bool expectedApplied)
    {
        var (sql, applied) = _service.ApplyAutoLimit(input, limit);

        applied.Should().Be(expectedApplied);
        sql.Should().Be(expectedSql);
    }

    [Theory]
    [InlineData("SELECT * FROM users LIMIT 50", 1000)]
    [InlineData("SELECT * FROM users limit 100;", 1000)]
    [InlineData("SELECT * FROM users FETCH FIRST 25 ROWS ONLY", 1000)]
    [InlineData("SELECT * FROM users FETCH NEXT 50 ROW ONLY;", 1000)]
    public void ApplyAutoLimit_QueryWithExistingLimit_DoesNotModify(string input, int limit)
    {
        var (sql, applied) = _service.ApplyAutoLimit(input, limit);

        applied.Should().BeFalse();
        sql.Should().Be(input);
    }

    [Theory]
    [InlineData("INSERT INTO users (name) VALUES ('Alice');")]
    [InlineData("UPDATE users SET active = 0 WHERE id = 1;")]
    [InlineData("DELETE FROM users WHERE id = 1;")]
    [InlineData("DROP TABLE users;")]
    [InlineData("CREATE TABLE items (id INT);")]
    public void ApplyAutoLimit_NonSelectQueries_DoesNotModify(string input)
    {
        var (sql, applied) = _service.ApplyAutoLimit(input, 1000);

        applied.Should().BeFalse();
        sql.Should().Be(input);
    }

    [Fact]
    public void ApplyAutoLimit_MultipleStatements_DoesNotModify()
    {
        var input = "SELECT 1; SELECT 2;";
        var (sql, applied) = _service.ApplyAutoLimit(input, 1000);

        applied.Should().BeFalse();
        sql.Should().Be(input);
    }

    [Fact]
    public void ApplyAutoLimit_SemicolonInsideStringLiteral_CorrectlyAppliesLimit()
    {
        var input = "SELECT * FROM logs WHERE message = 'error; please retry';";
        var (sql, applied) = _service.ApplyAutoLimit(input, 1000);

        applied.Should().BeTrue();
        sql.Should().Be("SELECT * FROM logs WHERE message = 'error; please retry' LIMIT 1000;");
    }

    [Fact]
    public void ApplyAutoLimit_ZeroOrNegativeLimit_DoesNotModify()
    {
        var input = "SELECT * FROM users";
        var (sql, applied) = _service.ApplyAutoLimit(input, 0);

        applied.Should().BeFalse();
        sql.Should().Be(input);
    }
}
