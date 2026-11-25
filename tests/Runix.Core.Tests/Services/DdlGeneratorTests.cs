using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class DdlGeneratorTests
{
    private readonly DdlGeneratorService _generator = new();

    [Fact]
    public void GenerateCreateTableSql_SQLite_GeneratesValidSyntax()
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = "users" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true, IsNullable = false },
                new() { Name = "username", DataType = "VARCHAR(50)", IsPrimaryKey = false, IsNullable = false },
                new() { Name = "bio", DataType = "TEXT", IsPrimaryKey = false, IsNullable = true }
            }
        };

        var ddl = _generator.GenerateCreateTableSql(node, DatabaseType.SQLite);

        ddl.Should().Contain("CREATE TABLE \"users\"");
        ddl.Should().Contain("\"id\" INTEGER NOT NULL");
        ddl.Should().Contain("\"username\" TEXT NOT NULL");
        ddl.Should().Contain("\"bio\" TEXT");
        ddl.Should().Contain("PRIMARY KEY (\"id\")");
    }

    [Fact]
    public void GenerateFullSchemaSql_SQLite_IncludesForeignKeysInline()
    {
        var graph = new ErdGraphModel
        {
            Nodes = new List<ErdTableNode>
            {
                new()
                {
                    Table = new TableInfo { Name = "orders" },
                    Columns = new List<ColumnInfo>
                    {
                        new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                        new() { Name = "user_id", DataType = "INTEGER", IsPrimaryKey = false }
                    }
                }
            },
            Relationships = new List<ErdRelationshipLink>
            {
                new()
                {
                    SourceTable = "orders",
                    SourceColumn = "user_id",
                    TargetTable = "users",
                    TargetColumn = "id",
                    OnDelete = "CASCADE"
                }
            }
        };

        var ddl = _generator.GenerateFullSchemaSql(graph, DatabaseType.SQLite);

        ddl.Should().Contain("FOREIGN KEY (\"user_id\") REFERENCES \"users\"(\"id\")");
        ddl.Should().Contain("ON DELETE CASCADE");
    }

    [Fact]
    public void GenerateFullSchemaSql_Postgres_GeneratesAlterTableAddConstraint()
    {
        var graph = new ErdGraphModel
        {
            Nodes = new List<ErdTableNode>
            {
                new()
                {
                    Table = new TableInfo { Name = "posts" },
                    Columns = new List<ColumnInfo>
                    {
                        new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                        new() { Name = "user_id", DataType = "INTEGER", IsPrimaryKey = false }
                    }
                }
            },
            Relationships = new List<ErdRelationshipLink>
            {
                new()
                {
                    SourceTable = "posts",
                    SourceColumn = "user_id",
                    TargetTable = "users",
                    TargetColumn = "id",
                    OnUpdate = "CASCADE",
                    OnDelete = "SET NULL"
                }
            }
        };

        var ddl = _generator.GenerateFullSchemaSql(graph, DatabaseType.PostgreSQL);

        ddl.Should().Contain("CREATE TABLE \"posts\"");
        ddl.Should().Contain("ALTER TABLE \"posts\" ADD CONSTRAINT \"fk_posts_user_id\" FOREIGN KEY (\"user_id\") REFERENCES \"users\"(\"id\")");
        ddl.Should().Contain("ON UPDATE CASCADE ON DELETE SET NULL;");
    }

    [Fact]
    public void GenerateCreateTableSql_Oracle_MapsTypesAndUppercaseIdentifiersCorrectly()
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = "customers" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true, IsNullable = false },
                new() { Name = "name", DataType = "VARCHAR(100)", IsPrimaryKey = false, IsNullable = false },
                new() { Name = "is_active", DataType = "BOOLEAN", DefaultValue = "true", IsNullable = true },
                new() { Name = "balance", DataType = "DECIMAL(10,2)" },
                new() { Name = "created_at", DataType = "TIMESTAMP" }
            }
        };

        var ddl = _generator.GenerateCreateTableSql(node, DatabaseType.Oracle);

        ddl.Should().Contain("CREATE TABLE \"CUSTOMERS\"");
        ddl.Should().Contain("\"ID\" NUMBER(10) NOT NULL");
        ddl.Should().Contain("\"NAME\" VARCHAR2(255) NOT NULL");
        ddl.Should().Contain("\"IS_ACTIVE\" NUMBER(1) DEFAULT 1");
        ddl.Should().Contain("\"BALANCE\" NUMBER(12,2)");
        ddl.Should().Contain("\"CREATED_AT\" TIMESTAMP");
        ddl.Should().Contain("CONSTRAINT \"PK_CUSTOMERS\" PRIMARY KEY (\"ID\")");
    }

    [Fact]
    public void GenerateFullSchemaSql_Oracle_OmitsOnUpdateInForeignKeys()
    {
        var graph = new ErdGraphModel
        {
            Nodes = new List<ErdTableNode>
            {
                new()
                {
                    Table = new TableInfo { Name = "orders" },
                    Columns = new List<ColumnInfo>
                    {
                        new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                        new() { Name = "customer_id", DataType = "INTEGER", IsPrimaryKey = false }
                    }
                }
            },
            Relationships = new List<ErdRelationshipLink>
            {
                new()
                {
                    SourceTable = "orders",
                    SourceColumn = "customer_id",
                    TargetTable = "customers",
                    TargetColumn = "id",
                    OnUpdate = "CASCADE",
                    OnDelete = "CASCADE"
                }
            }
        };

        var ddl = _generator.GenerateFullSchemaSql(graph, DatabaseType.Oracle);

        ddl.Should().Contain("CREATE TABLE \"ORDERS\"");
        ddl.Should().Contain("ALTER TABLE \"ORDERS\" ADD CONSTRAINT \"FK_ORDERS_CUSTOMER_ID\" FOREIGN KEY (\"CUSTOMER_ID\") REFERENCES \"CUSTOMERS\"(\"ID\") ON DELETE CASCADE;");
        ddl.Should().NotContain("ON UPDATE");
    }
}
