using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class SqliteDatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ConnectionConfig _config;
    private readonly SqliteDatabaseService _service;

    public SqliteDatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_test_{Guid.NewGuid():N}.db");
        _config = new ConnectionConfig
        {
            Name = "Test SQLite Database",
            Type = DatabaseType.SQLite,
            FilePath = _dbPath
        };
        _service = new SqliteDatabaseService(_config);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ConnectAsync_And_TestConnectionAsync_Succeeds()
    {
        var testSuccess = await _service.TestConnectionAsync();
        testSuccess.Should().BeTrue();

        await _service.ConnectAsync();
        _service.IsConnected.Should().BeTrue();

        await _service.DisconnectAsync();
        _service.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteQueryAsync_CreateTableAndInsert_ExecutesSuccessfully()
    {
        await _service.ConnectAsync();

        var createResult = await _service.ExecuteQueryAsync(
            "CREATE TABLE cheeses (id INTEGER PRIMARY KEY, name TEXT NOT NULL, price REAL);");
        createResult.Success.Should().BeTrue();

        var insertResult = await _service.ExecuteQueryAsync(
            "INSERT INTO cheeses (name, price) VALUES ('Gouda', 9.99), ('Cheddar', 14.50);");
        insertResult.Success.Should().BeTrue();
        insertResult.AffectedRows.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteQueryAsync_SelectQuery_ReturnsColumnsAndRows()
    {
        await _service.ConnectAsync();
        await _service.ExecuteQueryAsync("CREATE TABLE items (id INTEGER PRIMARY KEY, label TEXT);");
        await _service.ExecuteQueryAsync("INSERT INTO items (label) VALUES ('Alpha'), ('Beta'), ('Gamma');");

        var result = await _service.ExecuteQueryAsync("SELECT id, label FROM items ORDER BY id ASC;");

        result.Success.Should().BeTrue();
        result.Columns.Should().BeEquivalentTo(new[] { "id", "label" });
        result.Rows.Should().HaveCount(3);
        result.Rows[0][1]?.ToString().Should().Be("Alpha");
        result.Rows[1][1]?.ToString().Should().Be("Beta");
        result.Rows[2][1]?.ToString().Should().Be("Gamma");
    }

    [Fact]
    public async Task GetTableDataAsync_PaginationAndFilter_ReturnsAccurateSubset()
    {
        await _service.ConnectAsync();
        await _service.ExecuteQueryAsync("CREATE TABLE logs (id INTEGER PRIMARY KEY, level TEXT, message TEXT);");
        await _service.ExecuteQueryAsync(@"
            INSERT INTO logs (level, message) VALUES 
            ('INFO', 'Server started'),
            ('WARN', 'High memory'),
            ('INFO', 'Request processed'),
            ('ERROR', 'Disk full'),
            ('INFO', 'Shutdown');");

        // Page 1: 2 items
        var page1 = await _service.GetTableDataAsync("logs", offset: 0, limit: 2);
        page1.Success.Should().BeTrue();
        page1.Rows.Should().HaveCount(2);

        // Page 2: 2 items
        var page2 = await _service.GetTableDataAsync("logs", offset: 2, limit: 2);
        page2.Success.Should().BeTrue();
        page2.Rows.Should().HaveCount(2);

        // Filter: only INFO
        var filtered = await _service.GetTableDataAsync("logs", offset: 0, limit: 10, filter: "level = 'INFO'");
        filtered.Success.Should().BeTrue();
        filtered.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetTablesAsync_DiscoversCreatedTables()
    {
        await _service.ConnectAsync();
        await _service.ExecuteQueryAsync("CREATE TABLE alpha (id INT);");
        await _service.ExecuteQueryAsync("CREATE TABLE beta (id INT);");

        var tables = await _service.GetTablesAsync();

        tables.Should().Contain(t => t.Name == "alpha");
        tables.Should().Contain(t => t.Name == "beta");
    }

    [Fact]
    public async Task GetTableColumnsAsync_And_GetTableDdlAsync_ReturnsSchemaMetadata()
    {
        await _service.ConnectAsync();
        await _service.ExecuteQueryAsync(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                stock INTEGER DEFAULT 0
            );");

        var columns = await _service.GetTableColumnsAsync("products");

        columns.Should().HaveCount(3);
        columns.Should().Contain(c => c.Name == "id" && c.IsPrimaryKey);
        columns.Should().Contain(c => c.Name == "title" && !c.IsNullable);
        columns.Should().Contain(c => c.Name == "stock" && c.DefaultValue != null);

        var ddl = await _service.GetTableDdlAsync("products");
        ddl.Should().Contain("CREATE TABLE products");
    }

    [Fact]
    public async Task ExecuteQueryAsync_InvalidSql_ReturnsFailureWithoutException()
    {
        await _service.ConnectAsync();

        var result = await _service.ExecuteQueryAsync("SELECT * FROM non_existent_table_xyz;");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }
}
