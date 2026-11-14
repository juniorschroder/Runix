using System.Collections.Generic;
using Runix.Core.Models;
using Runix.Infrastructure.Migrations;
using Xunit;

namespace Runix.Infrastructure.Tests.Migrations;

public class MigrationGeneratorTests
{
    private readonly PostgresMigrationGenerator _postgresGenerator = new();
    private readonly SqliteMigrationGenerator _sqliteGenerator = new();

    [Fact]
    public void PostgresGenerator_ShouldGenerateValidCreateTableSql()
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = "customers", Schema = "public" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "SERIAL", IsPrimaryKey = true },
                new() { Name = "email", DataType = "VARCHAR(255)", IsNullable = false },
                new() { Name = "points", DataType = "INTEGER", IsNullable = true, DefaultValue = "0" }
            }
        };

        var sql = _postgresGenerator.GenerateCreateTable(node);

        Assert.Contains("CREATE TABLE \"customers\" (", sql);
        Assert.Contains("\"id\" SERIAL NOT NULL", sql);
        Assert.Contains("\"email\" VARCHAR(255) NOT NULL", sql);
        Assert.Contains("\"points\" INTEGER DEFAULT 0", sql);
        Assert.Contains("PRIMARY KEY (\"id\")", sql);
    }

    [Fact]
    public void PostgresGenerator_ShouldGenerateGranularAlterStatements()
    {
        var plan = new MigrationPlan
        {
            DatabaseType = DatabaseType.PostgreSQL,
            Operations = new List<MigrationOperation>
            {
                new()
                {
                    Type = MigrationOperationType.AddColumn,
                    TableName = "customers",
                    Column = new ColumnInfo { Name = "phone", DataType = "VARCHAR(50)", IsNullable = true }
                },
                new()
                {
                    Type = MigrationOperationType.DropColumn,
                    TableName = "customers",
                    Column = new ColumnInfo { Name = "fax" }
                },
                new()
                {
                    Type = MigrationOperationType.AddForeignKey,
                    TableName = "orders",
                    ForeignKey = new ForeignKeyDefinition
                    {
                        SourceTable = "orders",
                        SourceColumn = "customer_id",
                        TargetTable = "customers",
                        TargetColumn = "id",
                        OnDelete = "CASCADE",
                        OnUpdate = "RESTRICT"
                    }
                }
            }
        };

        var sql = _postgresGenerator.GenerateSql(plan);

        Assert.Contains("ALTER TABLE \"customers\" ADD COLUMN \"phone\" VARCHAR(50);", sql);
        Assert.Contains("ALTER TABLE \"customers\" DROP COLUMN \"fax\" CASCADE;", sql);
        Assert.Contains("ALTER TABLE \"orders\" ADD CONSTRAINT \"fk_orders_customer_id\" FOREIGN KEY (\"customer_id\") REFERENCES \"customers\"(\"id\") ON UPDATE RESTRICT ON DELETE CASCADE;", sql);
    }

    [Fact]
    public void SqliteGenerator_ShouldGenerateCreateTableWithInlineFk()
    {
        var plan = new MigrationPlan
        {
            DatabaseType = DatabaseType.SQLite,
            Operations = new List<MigrationOperation>
            {
                new()
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "items",
                    TargetColumns = new List<ColumnInfo>
                    {
                        new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                        new() { Name = "order_id", DataType = "INTEGER", IsNullable = false }
                    },
                    TargetForeignKeys = new List<ForeignKeyDefinition>
                    {
                        new()
                        {
                            SourceTable = "items",
                            SourceColumn = "order_id",
                            TargetTable = "orders",
                            TargetColumn = "id",
                            OnDelete = "CASCADE"
                        }
                    }
                }
            }
        };

        var sql = _sqliteGenerator.GenerateSql(plan);

        Assert.Contains("CREATE TABLE \"items\" (", sql);
        Assert.Contains("PRIMARY KEY (\"id\")", sql);
        Assert.Contains("FOREIGN KEY (\"order_id\") REFERENCES \"orders\"(\"id\") ON UPDATE NO ACTION ON DELETE CASCADE", sql);
    }

    [Fact]
    public void SqliteGenerator_ShouldGenerateSafeRebuildScript()
    {
        var op = new MigrationOperation
        {
            Type = MigrationOperationType.RebuildTableSqlite,
            TableName = "products",
            ExistingColumns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT" },
                new() { Name = "deprecated_sku", DataType = "TEXT" }
            },
            TargetColumns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT" },
                new() { Name = "sku", DataType = "TEXT", IsNullable = false, DefaultValue = "'N/A'" }
            }
        };

        var sql = _sqliteGenerator.GenerateSafeRebuildScript(op);

        Assert.Contains("PRAGMA foreign_keys = OFF;", sql);
        Assert.Contains("CREATE TABLE \"products_runix_new\" (", sql);
        Assert.Contains("INSERT INTO \"products_runix_new\" (\"id\", \"name\")", sql);
        Assert.Contains("SELECT \"id\", \"name\" FROM \"products\";", sql);
        Assert.Contains("DROP TABLE \"products\";", sql);
        Assert.Contains("ALTER TABLE \"products_runix_new\" RENAME TO \"products\";", sql);
        Assert.Contains("PRAGMA foreign_key_check;", sql);
        Assert.Contains("PRAGMA foreign_keys = ON;", sql);
    }
}
