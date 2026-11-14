using FluentAssertions;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class TableInfoTests
{
    [Fact]
    public void TableInfo_TypeTable_ReturnsTableIcon()
    {
        var table = new TableInfo
        {
            Name = "customers",
            Type = "BASE TABLE"
        };

        table.Icon.Should().Be("🗄");
        table.IsView.Should().BeFalse();
    }

    [Fact]
    public void TableInfo_TypeView_ReturnsViewIcon()
    {
        var table = new TableInfo
        {
            Name = "active_customers_view",
            Type = "VIEW"
        };

        table.Icon.Should().Be("👁");
        table.IsView.Should().BeTrue();
    }

    [Theory]
    [InlineData("public", "users", "users")]
    [InlineData("main", "users", "users")]
    [InlineData("", "users", "users")]
    [InlineData("analytics", "daily_metrics", "analytics.daily_metrics")]
    [InlineData("auth", "tokens", "auth.tokens")]
    public void TableInfo_DisplayName_FormatsSchemaProperly(string schema, string tableName, string expected)
    {
        var table = new TableInfo
        {
            Schema = schema,
            Name = tableName
        };

        table.DisplayName.Should().Be(expected);
    }

    [Fact]
    public void ColumnInfo_NullableAndPrimaryKeyFlags_FormatTextsCorrectly()
    {
        var pkCol = new ColumnInfo
        {
            OrdinalPosition = 1,
            Name = "id",
            DataType = "INTEGER",
            IsNullable = false,
            IsPrimaryKey = true
        };

        pkCol.NullableText.Should().Be("NO");
        pkCol.KeyText.Should().Be("🔑 PK");

        var nullableCol = new ColumnInfo
        {
            OrdinalPosition = 2,
            Name = "middle_name",
            DataType = "TEXT",
            IsNullable = true,
            IsPrimaryKey = false
        };

        nullableCol.NullableText.Should().Be("YES");
        nullableCol.KeyText.Should().BeEmpty();

        var fkCol = new ColumnInfo
        {
            OrdinalPosition = 3,
            Name = "country_id",
            DataType = "INTEGER",
            IsNullable = false,
            IsPrimaryKey = false,
            IsForeignKey = true,
            ForeignKeyReference = "countries(id)"
        };

        fkCol.KeyText.Should().Be("🔗 FK → countries(id)");

        var compositePkFk = new ColumnInfo
        {
            OrdinalPosition = 4,
            Name = "order_id",
            DataType = "INTEGER",
            IsNullable = false,
            IsPrimaryKey = true,
            IsForeignKey = true,
            ForeignKeyReference = "orders(id)"
        };

        compositePkFk.KeyText.Should().Be("🔑 PK, 🔗 FK");
    }

    [Fact]
    public void TableIndexInfo_Properties_FormatCorrectly()
    {
        var idx = new TableIndexInfo
        {
            Name = "idx_users_email",
            TableName = "users",
            Columns = "email",
            IsUnique = true,
            IsPrimaryKey = false,
            Method = "BTREE",
            Definition = "CREATE UNIQUE INDEX idx_users_email ON users(email);"
        };

        idx.UniqueText.Should().Be("YES");
        idx.PrimaryText.Should().BeEmpty();
        idx.ToString().Should().Be("idx_users_email (email)");

        var pkIdx = new TableIndexInfo
        {
            Name = "pk_users",
            TableName = "users",
            Columns = "id",
            IsUnique = true,
            IsPrimaryKey = true,
            Method = "BTREE"
        };

        pkIdx.PrimaryText.Should().Be("🔑 PK");
    }
}
