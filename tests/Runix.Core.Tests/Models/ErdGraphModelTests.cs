using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Models;

public class ErdGraphModelTests
{
    [Fact]
    public void ErdTableNode_Height_ShouldScaleWithColumns()
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = "users" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "email", DataType = "TEXT" },
                new() { Name = "created_at", DataType = "TEXT" }
            }
        };

        node.TableName.Should().Be("users");
        node.Height.Should().BeGreaterThan(100);
        node.Width.Should().Be(250);
    }

    [Fact]
    public void ErdLayoutService_InferCardinality_OneToOne_WhenFkIsPrimaryKeyOrUnique()
    {
        var sourceCols = new List<ColumnInfo>
        {
            new() { Name = "user_id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "bio", DataType = "TEXT" }
        };

        var fk = new ForeignKeyInfo
        {
            SourceTable = "profiles",
            SourceColumn = "user_id",
            TargetTable = "users",
            TargetColumn = "id"
        };

        var cardinality = ErdLayoutService.InferCardinality(fk, sourceCols, new List<ForeignKeyInfo> { fk });
        cardinality.Should().Be(ErdCardinality.OneToOne);
    }

    [Fact]
    public void ErdLayoutService_InferCardinality_OneToMany_StandardFk()
    {
        var sourceCols = new List<ColumnInfo>
        {
            new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "customer_id", DataType = "INTEGER", IsPrimaryKey = false },
            new() { Name = "amount", DataType = "REAL" }
        };

        var fk = new ForeignKeyInfo
        {
            SourceTable = "orders",
            SourceColumn = "customer_id",
            TargetTable = "customers",
            TargetColumn = "id"
        };

        var cardinality = ErdLayoutService.InferCardinality(fk, sourceCols, new List<ForeignKeyInfo> { fk });
        cardinality.Should().Be(ErdCardinality.OneToMany);
    }

    [Fact]
    public void ErdLayoutService_InferCardinality_ManyToMany_JunctionTable()
    {
        var sourceCols = new List<ColumnInfo>
        {
            new() { Name = "student_id", DataType = "INTEGER", IsPrimaryKey = true },
            new() { Name = "course_id", DataType = "INTEGER", IsPrimaryKey = true }
        };

        var fks = new List<ForeignKeyInfo>
        {
            new() { SourceTable = "enrollments", SourceColumn = "student_id", TargetTable = "students", TargetColumn = "id" },
            new() { SourceTable = "enrollments", SourceColumn = "course_id", TargetTable = "courses", TargetColumn = "id" }
        };

        var cardinality = ErdLayoutService.InferCardinality(fks[0], sourceCols, fks);
        cardinality.Should().Be(ErdCardinality.ManyToMany);
    }

    [Fact]
    public void ErdLayoutService_ApplyTopologicalLayout_DistributesNodesHierarchically()
    {
        var graph = new ErdGraphModel
        {
            Nodes = new List<ErdTableNode>
            {
                new() { Table = new TableInfo { Name = "authors" } },
                new() { Table = new TableInfo { Name = "books" } },
                new() { Table = new TableInfo { Name = "reviews" } }
            },
            Relationships = new List<ErdRelationshipLink>
            {
                new() { SourceTable = "books", SourceColumn = "author_id", TargetTable = "authors", TargetColumn = "id" },
                new() { SourceTable = "reviews", SourceColumn = "book_id", TargetTable = "books", TargetColumn = "id" }
            }
        };

        ErdLayoutService.ApplyTopologicalLayout(graph);

        var authors = graph.FindNode("authors")!;
        var books = graph.FindNode("books")!;
        var reviews = graph.FindNode("reviews")!;

        // Authors is independent -> col 0
        authors.X.Should().Be(40);
        // Books depends on authors -> col 1
        books.X.Should().BeGreaterThan(authors.X);
        // Reviews depends on books -> col 2
        reviews.X.Should().BeGreaterThan(books.X);
    }
}
