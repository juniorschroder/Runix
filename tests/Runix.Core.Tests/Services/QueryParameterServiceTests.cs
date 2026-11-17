using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class QueryParameterServiceTests
{
    private readonly QueryParameterService _service = new();

    [Fact]
    public void ExtractParameters_WithColonPlaceholders_ExtractsNamesCorrectly()
    {
        var sql = "SELECT * FROM orders WHERE customer_id = :cust_id AND amount > :min_amount;";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(2);
        parameters[0].Should().Be("cust_id");
        parameters[1].Should().Be("min_amount");
    }

    [Fact]
    public void ExtractParameters_WithMustachePlaceholders_ExtractsNamesCorrectly()
    {
        var sql = "SELECT * FROM logs WHERE created_at >= {{start_date}} AND created_at <= {{ end_date }};";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(2);
        parameters[0].Should().Be("start_date");
        parameters[1].Should().Be("end_date");
    }

    [Fact]
    public void ExtractParameters_MixedColonAndMustache_PreservesFirstAppearanceOrder()
    {
        var sql = "SELECT * FROM users WHERE id = :user_id AND org = {{org_id}} AND role = :role;";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().Equal("user_id", "org_id", "role");
    }

    [Fact]
    public void ExtractParameters_DuplicatePlaceholders_ReturnsUniqueList()
    {
        var sql = "SELECT * FROM items WHERE owner_id = :user_id OR created_by = :user_id OR modified_by = {{user_id}};";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(1);
        parameters[0].Should().Be("user_id");
    }

    [Fact]
    public void ExtractParameters_WithPostgresTypeCasts_IgnoresDoubleColon()
    {
        var sql = "SELECT id::int, created_at::date, amount::numeric(10,2) FROM orders WHERE customer_id = :cust_id;";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(1);
        parameters[0].Should().Be("cust_id");
    }

    [Fact]
    public void ExtractParameters_InsideSingleQuotes_IgnoresStringLiterals()
    {
        var sql = "SELECT ':not_a_param', '{{also_not_param}}' FROM users WHERE id = :real_id;";
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(1);
        parameters[0].Should().Be("real_id");
    }

    [Fact]
    public void ExtractParameters_InsideComments_IgnoresComments()
    {
        var sql = """
            -- Filter by :commented_id and {{commented_mustache}}
            /* Multiline :blocked_id and {{blocked_mustache}} */
            SELECT * FROM accounts WHERE id = :actual_id;
            """;
        var parameters = _service.ExtractParameters(sql);

        parameters.Should().HaveCount(1);
        parameters[0].Should().Be("actual_id");
    }

    [Fact]
    public void HasParameters_ReturnsTrueOnlyWhenParametersExist()
    {
        _service.HasParameters("SELECT 1;").Should().BeFalse();
        _service.HasParameters("SELECT * FROM users WHERE id = :id;").Should().BeTrue();
        _service.HasParameters("SELECT * FROM users WHERE id = {{id}};").Should().BeTrue();
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.14", "3.14")]
    [InlineData("-100", "-100")]
    [InlineData("true", "TRUE")]
    [InlineData("FALSE", "FALSE")]
    [InlineData("null", "NULL")]
    [InlineData("'already_quoted'", "'already_quoted'")]
    [InlineData("hello world", "'hello world'")]
    [InlineData("O'Reilly", "'O''Reilly'")]
    [InlineData("", "''")]
    [InlineData(null, "NULL")]
    public void FormatSqlLiteral_FormatsValuesCorrectly(string? input, string expected)
    {
        _service.FormatSqlLiteral(input).Should().Be(expected);
    }

    [Fact]
    public void SubstituteParameters_ReplacesPlaceholdersAccurately()
    {
        var sql = "SELECT id::text FROM users WHERE id = :id AND code = {{code}} AND active = :is_active;";
        var values = new Dictionary<string, string>
        {
            ["id"] = "999",
            ["code"] = "PRD-2026",
            ["is_active"] = "true"
        };

        var substituted = _service.SubstituteParameters(sql, values);

        substituted.Should().Be("SELECT id::text FROM users WHERE id = 999 AND code = 'PRD-2026' AND active = TRUE;");
    }

    [Fact]
    public void History_StoresAndRetrievesRecentValuesWithoutDuplicates()
    {
        var service = new QueryParameterService();
        service.SaveHistory("user_id", "10");
        service.SaveHistory("user_id", "20");
        service.SaveHistory("user_id", "10"); // should move to front

        var history = service.GetHistory("user_id");
        history.Should().Equal("10", "20");
    }
}
