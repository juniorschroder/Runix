using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Core.Models.MockData;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Services;
using Xunit;

namespace Runix.Infrastructure.Tests.MockData;

public class MockDataGeneratorServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ConnectionConfig _config;
    private readonly MockDataGeneratorService _service;

    public MockDataGeneratorServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_mock_test_{Guid.NewGuid():N}.db");
        _config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _dbPath,
            Name = "MockTestDb"
        };
        _service = new MockDataGeneratorService();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void InferRules_DetectsSemanticTypesCorrectly()
    {
        var table = new TableInfo { Name = "customers" };
        var columns = new List<ColumnInfo>
        {
            new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "full_name", DataType = "TEXT" },
            new() { Name = "email_address", DataType = "TEXT" },
            new() { Name = "phone_number", DataType = "TEXT" },
            new() { Name = "created_at", DataType = "DATETIME" },
            new() { Name = "order_total", DataType = "DECIMAL" },
            new() { Name = "is_active", DataType = "BOOLEAN" },
            new() { Name = "session_uuid", DataType = "TEXT" },
            new() { Name = "shipping_address", DataType = "TEXT" },
            new() { Name = "city_name", DataType = "TEXT" },
            new() { Name = "country_origin", DataType = "TEXT" },
            new() { Name = "postal_code", DataType = "TEXT" }
        };

        var rules = _service.InferRules(table, columns, new List<ForeignKeyInfo>());

        var fullNameRule = rules.First(r => r.ColumnName == "full_name");
        fullNameRule.SelectedGenerator.Should().Be(GeneratorType.Name);

        var emailRule = rules.First(r => r.ColumnName == "email_address");
        emailRule.SelectedGenerator.Should().Be(GeneratorType.Email);

        var phoneRule = rules.First(r => r.ColumnName == "phone_number");
        phoneRule.SelectedGenerator.Should().Be(GeneratorType.Phone);

        var dateRule = rules.First(r => r.ColumnName == "created_at");
        dateRule.SelectedGenerator.Should().Be(GeneratorType.DateTime);

        var priceRule = rules.First(r => r.ColumnName == "order_total");
        priceRule.SelectedGenerator.Should().Be(GeneratorType.Decimal);

        var boolRule = rules.First(r => r.ColumnName == "is_active");
        boolRule.SelectedGenerator.Should().Be(GeneratorType.Boolean);

        var uuidRule = rules.First(r => r.ColumnName == "session_uuid");
        uuidRule.SelectedGenerator.Should().Be(GeneratorType.Uuid);

        var addressRule = rules.First(r => r.ColumnName == "shipping_address");
        addressRule.SelectedGenerator.Should().Be(GeneratorType.Address);

        var cityRule = rules.First(r => r.ColumnName == "city_name");
        cityRule.SelectedGenerator.Should().Be(GeneratorType.City);

        var countryRule = rules.First(r => r.ColumnName == "country_origin");
        countryRule.SelectedGenerator.Should().Be(GeneratorType.Country);

        var zipRule = rules.First(r => r.ColumnName == "postal_code");
        zipRule.SelectedGenerator.Should().Be(GeneratorType.ZipCode);
    }

    [Fact]
    public void InferRules_AutoIncrement_MarksSkipped()
    {
        var table = new TableInfo { Name = "orders" };
        var columns = new List<ColumnInfo>
        {
            new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "title", DataType = "TEXT" }
        };

        var rules = _service.InferRules(table, columns, new List<ForeignKeyInfo>());

        var idRule = rules.First(r => r.ColumnName == "id");
        idRule.IsPrimaryKey.Should().BeTrue();
        idRule.IsAutoIncrement.Should().BeTrue();
        idRule.IsSkipped.Should().BeTrue();

        var titleRule = rules.First(r => r.ColumnName == "title");
        titleRule.IsSkipped.Should().BeFalse();
    }

    [Fact]
    public void InferRules_ForeignKey_IdentifiesParentRelationship()
    {
        var table = new TableInfo { Name = "order_items" };
        var columns = new List<ColumnInfo>
        {
            new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "order_id", DataType = "INTEGER" },
            new() { Name = "product_name", DataType = "TEXT" }
        };

        var fks = new List<ForeignKeyInfo>
        {
            new()
            {
                SourceTable = "order_items",
                SourceColumn = "order_id",
                TargetTable = "orders",
                TargetColumn = "id"
            }
        };

        var rules = _service.InferRules(table, columns, fks);

        var orderIdRule = rules.First(r => r.ColumnName == "order_id");
        orderIdRule.SelectedGenerator.Should().Be(GeneratorType.ForeignKeySample);
        orderIdRule.ForeignKeyTargetTable.Should().Be("orders");
        orderIdRule.ForeignKeyTargetColumn.Should().Be("id");
    }

    [Fact]
    public void GenerateRows_ForeignKeySampling_OnlyUsesProvidedParentKeys()
    {
        var config = new TableMockConfig
        {
            TableName = "order_items",
            RowCount = 20,
            Dialect = DatabaseType.SQLite,
            Rules = new List<ColumnGeneratorRule>
            {
                new()
                {
                    ColumnName = "order_id",
                    SelectedGenerator = GeneratorType.ForeignKeySample,
                    ForeignKeyTargetTable = "orders",
                    ForeignKeyTargetColumn = "id",
                    SampledValues = new List<object> { 101, 202, 303 }
                },
                new()
                {
                    ColumnName = "product_name",
                    SelectedGenerator = GeneratorType.Name
                }
            }
        };

        var rows = _service.GenerateRows(config, 20);

        rows.Should().HaveCount(20);
        foreach (var row in rows)
        {
            row["order_id"].Should().Match(val => (int)val! == 101 || (int)val! == 202 || (int)val! == 303);
            row["product_name"].Should().NotBeNull();
        }
    }

    [Fact]
    public void GenerateRows_NullPercentage_HonorsZeroAndNonZero()
    {
        var config = new TableMockConfig
        {
            TableName = "test_table",
            RowCount = 100,
            Rules = new List<ColumnGeneratorRule>
            {
                new()
                {
                    ColumnName = "never_null",
                    IsNullable = false,
                    NullPercentage = 0,
                    SelectedGenerator = GeneratorType.Name
                },
                new()
                {
                    ColumnName = "always_null",
                    IsNullable = true,
                    NullPercentage = 100,
                    SelectedGenerator = GeneratorType.Name
                }
            }
        };

        var rows = _service.GenerateRows(config, 100);

        rows.All(r => r["never_null"] != null).Should().BeTrue();
        rows.All(r => r["always_null"] == null).Should().BeTrue();
    }

    [Fact]
    public void SafeChunking_CalculatesCorrectBatchSizesForSqlServerAndSqlite()
    {
        // SQL Server: 2000 / cols (max 100)
        MockDataGeneratorService.CalculateSafeBatchSize(10, DatabaseType.SqlServer).Should().Be(100);
        MockDataGeneratorService.CalculateSafeBatchSize(50, DatabaseType.SqlServer).Should().Be(40);
        MockDataGeneratorService.CalculateSafeBatchSize(100, DatabaseType.SqlServer).Should().Be(20);

        // SQLite: 900 / cols (max 200)
        MockDataGeneratorService.CalculateSafeBatchSize(5, DatabaseType.SQLite).Should().Be(180);
        MockDataGeneratorService.CalculateSafeBatchSize(30, DatabaseType.SQLite).Should().Be(30);

        // PostgreSQL: max 500
        MockDataGeneratorService.CalculateSafeBatchSize(5, DatabaseType.PostgreSQL).Should().Be(500);
    }

    [Fact]
    public async Task SeedBatchAsync_Sqlite_InsertsRowsSuccessfully()
    {
        var service = new SqliteDatabaseService(_config);
        await service.ConnectAsync();

        await service.ExecuteQueryAsync(@"
            CREATE TABLE members (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                full_name TEXT NOT NULL,
                email TEXT NOT NULL,
                points INTEGER NOT NULL
            );
        ");

        var config = new TableMockConfig
        {
            TableName = "members",
            RowCount = 25,
            Dialect = DatabaseType.SQLite,
            Rules = new List<ColumnGeneratorRule>
            {
                new() { ColumnName = "id", IsPrimaryKey = true, IsAutoIncrement = true, IsSkipped = true },
                new() { ColumnName = "full_name", SelectedGenerator = GeneratorType.Name },
                new() { ColumnName = "email", SelectedGenerator = GeneratorType.Email },
                new() { ColumnName = "points", SelectedGenerator = GeneratorType.Number, MinValue = 10, MaxValue = 500 }
            }
        };

        var progressReported = false;
        var progress = new Progress<MockDataProgress>(p =>
        {
            if (p.Percentage == 100) progressReported = true;
        });

        var result = await _service.SeedBatchAsync(service, config, progress);

        result.Success.Should().BeTrue();
        result.RowsInserted.Should().Be(25);
        result.ErrorMessage.Should().BeNull();
        progressReported.Should().BeTrue();

        // Verify count in database
        var countQuery = await service.ExecuteQueryAsync("SELECT COUNT(*) FROM members;");
        countQuery.Success.Should().BeTrue();
        Convert.ToInt32(countQuery.Rows[0][0]).Should().Be(25);
    }

    [Fact]
    public async Task SeedBatchAsync_EmptyParentForeignKey_RejectsInsertionWhenNonNullable()
    {
        var service = new SqliteDatabaseService(_config);
        await service.ConnectAsync();

        var config = new TableMockConfig
        {
            TableName = "child_table",
            RowCount = 10,
            Dialect = DatabaseType.SQLite,
            Rules = new List<ColumnGeneratorRule>
            {
                new()
                {
                    ColumnName = "parent_id",
                    SelectedGenerator = GeneratorType.ForeignKeySample,
                    ForeignKeyTargetTable = "parent_table",
                    ForeignKeyTargetColumn = "id",
                    ParentTableEmpty = true,
                    IsNullable = false
                }
            }
        };

        var result = await _service.SeedBatchAsync(service, config);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("parent_table");
        result.ErrorMessage.Should().Contain("está vazia");
    }

    [Fact]
    public void GenerateSqlScript_ProducesValidMultiRowInsertsPerDialect()
    {
        var config = new TableMockConfig
        {
            TableName = "users",
            RowCount = 2,
            Rules = new List<ColumnGeneratorRule>
            {
                new() { ColumnName = "id", SelectedGenerator = GeneratorType.Sequence, SequenceStart = 1 },
                new() { ColumnName = "name", SelectedGenerator = GeneratorType.Name },
                new() { ColumnName = "is_active", SelectedGenerator = GeneratorType.Boolean }
            }
        };

        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["name"] = "Alice", ["is_active"] = true },
            new() { ["id"] = 2, ["name"] = "Bob O'Connor", ["is_active"] = false }
        };

        // PostgreSQL
        var pgScript = _service.GenerateSqlScript(config, rows, DatabaseType.PostgreSQL);
        pgScript.Should().Contain("INSERT INTO \"users\" (\"id\", \"name\", \"is_active\") VALUES");
        pgScript.Should().Contain("'Bob O''Connor'");
        pgScript.Should().Contain("TRUE");
        pgScript.Should().Contain("FALSE");

        // MySQL
        var myScript = _service.GenerateSqlScript(config, rows, DatabaseType.MySQL);
        myScript.Should().Contain("INSERT INTO `users` (`id`, `name`, `is_active`) VALUES");

        // SQL Server
        var sqlServerScript = _service.GenerateSqlScript(config, rows, DatabaseType.SqlServer);
        sqlServerScript.Should().Contain("INSERT INTO [users] ([id], [name], [is_active]) VALUES");
        sqlServerScript.Should().Contain("1"); // boolean as 1
    }
}
