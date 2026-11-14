using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Export;
using Xunit;

namespace Runix.Infrastructure.Tests.Export;

public class DataExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DataExportService _exportService;

    public DataExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"runix_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _exportService = new DataExportService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Export_ToCsv_WithCommaAndSemicolon_EscapesProperly()
    {
        var columns = new List<string> { "id", "name", "notes", "price" };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { 1, "Brie de Meaux", "Smooth, creamy texture", 18.5m },
            new object?[] { 2, "Gouda \"Aged\"", "Aged; with crystals\nand rich flavor", 24.0m },
            new object?[] { 3, "Roquefort", null, 29.9m }
        };

        // 1. Comma CSV
        var csvPath = Path.Combine(_tempDir, "cheeses.csv");
        var csvOptions = new ExportOptions
        {
            Format = ExportFormat.Csv,
            FilePath = csvPath,
            CsvDelimiter = CsvDelimiter.Comma,
            IncludeHeader = true
        };

        var result = await _exportService.ExportDataAsync(columns, rows, csvOptions);
        result.Success.Should().BeTrue();
        result.RowsExported.Should().Be(3);
        File.Exists(csvPath).Should().BeTrue();

        var csvText = await File.ReadAllTextAsync(csvPath);
        csvText.Should().Contain("id,name,notes,price");
        csvText.Should().Contain("\"Smooth, creamy texture\"");
        csvText.Should().Contain("\"Gouda \"\"Aged\"\"\"");

        // 2. Semicolon CSV
        var semiPath = Path.Combine(_tempDir, "cheeses_semi.csv");
        var semiOptions = new ExportOptions
        {
            Format = ExportFormat.Csv,
            FilePath = semiPath,
            CsvDelimiter = CsvDelimiter.Semicolon,
            IncludeHeader = true
        };

        var semiResult = await _exportService.ExportDataAsync(columns, rows, semiOptions);
        semiResult.Success.Should().BeTrue();
        var semiText = await File.ReadAllTextAsync(semiPath);
        semiText.Should().Contain("id;name;notes;price");
        semiText.Should().Contain("\"Aged; with crystals");
    }

    [Fact]
    public async Task Export_ToJson_ArrayOfObjects_And_Tabular()
    {
        var columns = new List<string> { "id", "name", "price" };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { 1, "Parmigiano", 32.5 },
            new object?[] { 2, "Cheddar", 14.0 }
        };

        // 1. Array of Objects
        var jsonObjPath = Path.Combine(_tempDir, "objects.json");
        var optObj = new ExportOptions
        {
            Format = ExportFormat.Json,
            FilePath = jsonObjPath,
            JsonStyle = JsonFormatStyle.ArrayOfObjects,
            JsonIndented = true
        };

        var resObj = await _exportService.ExportDataAsync(columns, rows, optObj);
        resObj.Success.Should().BeTrue();
        resObj.RowsExported.Should().Be(2);

        var jsonText = await File.ReadAllTextAsync(jsonObjPath);
        using var docObj = JsonDocument.Parse(jsonText);
        docObj.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        docObj.RootElement.GetArrayLength().Should().Be(2);
        docObj.RootElement[0].GetProperty("name").GetString().Should().Be("Parmigiano");
        docObj.RootElement[0].GetProperty("price").GetDouble().Should().Be(32.5);

        // 2. Tabular
        var jsonTabPath = Path.Combine(_tempDir, "tabular.json");
        var optTab = new ExportOptions
        {
            Format = ExportFormat.Json,
            FilePath = jsonTabPath,
            JsonStyle = JsonFormatStyle.Tabular,
            JsonIndented = false
        };

        var resTab = await _exportService.ExportDataAsync(columns, rows, optTab);
        resTab.Success.Should().BeTrue();

        var tabText = await File.ReadAllTextAsync(jsonTabPath);
        using var docTab = JsonDocument.Parse(tabText);
        docTab.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        docTab.RootElement.GetProperty("columns").GetArrayLength().Should().Be(3);
        docTab.RootElement.GetProperty("rows").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Export_ToExcel_GeneratesValidNonEmptyXlsx()
    {
        var columns = new List<string> { "id", "title", "quantity", "active" };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { 101, "Gorgonzola Dolce", 45, true },
            new object?[] { 102, "Camembert de Normandie", 30, false },
            new object?[] { 103, "Manchego Curado", null, true }
        };

        var excelPath = Path.Combine(_tempDir, "inventory.xlsx");
        var options = new ExportOptions
        {
            Format = ExportFormat.Excel,
            FilePath = excelPath,
            SheetName = "Inventory"
        };

        var result = await _exportService.ExportDataAsync(columns, rows, options);
        result.Success.Should().BeTrue();
        result.RowsExported.Should().Be(3);
        File.Exists(excelPath).Should().BeTrue();

        var fileInfo = new FileInfo(excelPath);
        fileInfo.Length.Should().BeGreaterThan(1000); // Valid zip archive format
    }

    [Fact]
    public async Task Export_ToSqlDump_GeneratesBatchedInserts_WithDialectEscaping()
    {
        var columns = new List<string> { "id", "name", "price", "is_available", "signature" };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { 1, "Saint-Nectaire 'AOC'", 22.0, true, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } },
            new object?[] { 2, "Pecorino Romano", 19.5, false, null },
            new object?[] { 3, "Gruyere Reserve", 28.0, true, new byte[] { 0x01, 0x02 } }
        };

        // 1. SQLite dialect
        var sqliteDumpPath = Path.Combine(_tempDir, "sqlite_dump.sql");
        var sqliteOptions = new ExportOptions
        {
            Format = ExportFormat.SqlDump,
            FilePath = sqliteDumpPath,
            TableName = "cheeses",
            TargetDatabaseType = DatabaseType.SQLite,
            IncludeCreateTable = true,
            IncludeTransaction = true,
            BatchSize = 2 // test batching into 2 batches
        };

        var sqliteRes = await _exportService.ExportDataAsync(columns, rows, sqliteOptions);
        sqliteRes.Success.Should().BeTrue();
        sqliteRes.RowsExported.Should().Be(3);

        var sqliteSql = await File.ReadAllTextAsync(sqliteDumpPath);
        sqliteSql.Should().Contain("BEGIN TRANSACTION;");
        sqliteSql.Should().Contain("CREATE TABLE IF NOT EXISTS \"cheeses\"");
        sqliteSql.Should().Contain("INSERT INTO \"cheeses\"");
        sqliteSql.Should().Contain("'Saint-Nectaire ''AOC'''");
        sqliteSql.Should().Contain("X'DEADBEEF'");
        sqliteSql.Should().Contain("1"); // boolean true in SQLite
        sqliteSql.Should().Contain("COMMIT;");

        // 2. PostgreSQL dialect
        var pgDumpPath = Path.Combine(_tempDir, "postgres_dump.sql");
        var pgOptions = new ExportOptions
        {
            Format = ExportFormat.SqlDump,
            FilePath = pgDumpPath,
            TableName = "cheeses",
            TargetDatabaseType = DatabaseType.PostgreSQL,
            IncludeCreateTable = false,
            IncludeTransaction = true,
            BatchSize = 500
        };

        var pgRes = await _exportService.ExportDataAsync(columns, rows, pgOptions);
        pgRes.Success.Should().BeTrue();

        var pgSql = await File.ReadAllTextAsync(pgDumpPath);
        pgSql.Should().Contain("BEGIN;");
        pgSql.Should().NotContain("CREATE TABLE");
        pgSql.Should().Contain("TRUE"); // boolean true in Postgres
        pgSql.Should().Contain("'\\xDEADBEEF'"); // bytea in Postgres
        pgSql.Should().Contain("COMMIT;");
    }

    [Fact]
    public async Task Export_Cancellation_AbortsAndCleansUpFile()
    {
        var columns = new List<string> { "id", "val" };
        var rows = new List<IReadOnlyList<object?>>();
        for (int i = 0; i < 5000; i++)
        {
            rows.Add(new object?[] { i, $"value_{i}" });
        }

        var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        var path = Path.Combine(_tempDir, "canceled.csv");
        var options = new ExportOptions
        {
            Format = ExportFormat.Csv,
            FilePath = path
        };

        var result = await _exportService.ExportDataAsync(columns, rows, options, ct: cts.Token);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelada");
        File.Exists(path).Should().BeFalse();
    }
}
