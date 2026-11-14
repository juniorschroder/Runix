using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class SqliteFkIntrospectionTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _service;

    public SqliteFkIntrospectionTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_fk_test_{Guid.NewGuid():N}.db");
        var config = new ConnectionConfig
        {
            Name = "FK Test DB",
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        };
        _service = new SqliteDatabaseService(config);
    }

    private async Task SetupRelationalSchemaAsync()
    {
        await _service.ConnectAsync();

        const string ddl = @"
            PRAGMA foreign_keys = ON;

            CREATE TABLE authors (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL
            );

            CREATE TABLE books (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL,
                author_id INTEGER NOT NULL,
                FOREIGN KEY (author_id) REFERENCES authors(id) ON DELETE CASCADE
            );

            CREATE TABLE categories (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL
            );

            CREATE TABLE book_categories (
                book_id INTEGER NOT NULL,
                category_id INTEGER NOT NULL,
                PRIMARY KEY (book_id, category_id),
                FOREIGN KEY (book_id) REFERENCES books(id) ON DELETE CASCADE,
                FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE CASCADE
            );

            CREATE TABLE book_profiles (
                id INTEGER PRIMARY KEY,
                book_id INTEGER NOT NULL UNIQUE,
                bio TEXT,
                FOREIGN KEY (book_id) REFERENCES books(id)
            );
        ";

        var result = await _service.ExecuteQueryAsync(ddl);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task GetForeignKeysAsync_ReturnsAllConfiguredForeignKeys()
    {
        await SetupRelationalSchemaAsync();

        var allFks = await _service.GetForeignKeysAsync();
        allFks.Should().NotBeEmpty();

        // Books FK
        var bookFk = allFks.FirstOrDefault(f => f.SourceTable == "books");
        bookFk.Should().NotBeNull();
        bookFk!.SourceColumn.Should().Be("author_id");
        bookFk.TargetTable.Should().Be("authors");
        bookFk.TargetColumn.Should().Be("id");
        bookFk.OnDelete.Should().Be("CASCADE");

        // BookCategories has 2 FKs
        var junctionFks = allFks.Where(f => f.SourceTable == "book_categories").ToList();
        junctionFks.Should().HaveCount(2);

        // Filter by table
        var onlyBookFks = await _service.GetForeignKeysAsync("books");
        onlyBookFks.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetErdGraphAsync_ExtractsNodesRelationshipsAndCardinalities()
    {
        await SetupRelationalSchemaAsync();

        var graph = await _service.GetErdGraphAsync();

        graph.Nodes.Should().HaveCount(5);
        graph.Relationships.Should().HaveCount(4);

        // Check 1:N cardinality (books -> authors)
        var booksToAuthors = graph.Relationships.FirstOrDefault(r => r.SourceTable == "books" && r.TargetTable == "authors");
        booksToAuthors.Should().NotBeNull();
        booksToAuthors!.Cardinality.Should().Be(ErdCardinality.OneToMany);

        // Check 1:1 cardinality (book_profiles -> books where book_id is UNIQUE)
        var profileToBooks = graph.Relationships.FirstOrDefault(r => r.SourceTable == "book_profiles" && r.TargetTable == "books");
        profileToBooks.Should().NotBeNull();
        profileToBooks!.Cardinality.Should().Be(ErdCardinality.OneToOne);

        // Check N:M cardinality (book_categories junction table)
        var junctionLinks = graph.Relationships.Where(r => r.SourceTable == "book_categories").ToList();
        junctionLinks.Should().HaveCount(2);
        junctionLinks.All(l => l.Cardinality == ErdCardinality.ManyToMany).Should().BeTrue();

        // Check topological coordinates are assigned
        foreach (var node in graph.Nodes)
        {
            node.X.Should().BeGreaterOrEqualTo(40);
            node.Y.Should().BeGreaterOrEqualTo(40);
            node.Width.Should().Be(250);
            node.Height.Should().BeGreaterThan(90);
        }
    }

    [Fact]
    public async Task GetTableIndexesAsync_ReturnsIndexesWithColumnsAndUniqueness()
    {
        await SetupRelationalSchemaAsync();

        // Create custom index
        await _service.ExecuteQueryAsync("CREATE INDEX idx_books_title ON books(title);");
        await _service.ExecuteQueryAsync("CREATE UNIQUE INDEX idx_authors_name ON authors(name);");

        var bookIndexes = await _service.GetTableIndexesAsync("books");
        bookIndexes.Should().NotBeEmpty();

        var titleIdx = bookIndexes.FirstOrDefault(i => i.Name == "idx_books_title");
        titleIdx.Should().NotBeNull();
        titleIdx!.Columns.Should().Be("title");
        titleIdx.IsUnique.Should().BeFalse();

        var authorIndexes = await _service.GetTableIndexesAsync("authors");
        var nameIdx = authorIndexes.FirstOrDefault(i => i.Name == "idx_authors_name");
        nameIdx.Should().NotBeNull();
        nameIdx!.Columns.Should().Be("name");
        nameIdx.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableColumnsAsync_PopulatesPkAndFkMetadata()
    {
        await SetupRelationalSchemaAsync();

        var bookCols = await _service.GetTableColumnsAsync("books");
        bookCols.Should().HaveCount(3);

        var idCol = bookCols.First(c => c.Name == "id");
        idCol.IsPrimaryKey.Should().BeTrue();
        idCol.KeyText.Should().Be("🔑 PK");

        var authorIdCol = bookCols.First(c => c.Name == "author_id");
        authorIdCol.IsForeignKey.Should().BeTrue();
        authorIdCol.ForeignKeyReference.Should().Be("authors(id)");
        authorIdCol.KeyText.Should().Be("🔗 FK → authors(id)");
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch
        {
            // Ignore temp file deletion failure on test teardown
        }
    }
}
