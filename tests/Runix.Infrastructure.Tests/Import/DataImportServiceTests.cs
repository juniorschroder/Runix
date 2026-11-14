using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MiniExcelLibs;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Import;
using Xunit;

namespace Runix.Infrastructure.Tests.Import;

public class DataImportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DataImportService _importService;

    public DataImportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"runix_import_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _importService = new DataImportService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PreviewFileAsync_WithCsvCommaAndSemicolon_AutoDetectsDelimiterAndReturnsPreview()
    {
        // 1. Comma CSV
        var commaCsvPath = Path.Combine(_tempDir, "comma_sample.csv");
        await File.WriteAllTextAsync(commaCsvPath, "id,name,role\n1,Alice,Admin\n2,Bob,Developer\n3,Carol,Designer\n");

        var commaPreview = await _importService.PreviewFileAsync(commaCsvPath);
        commaPreview.DetectedDelimiter.Should().Be(',');
        commaPreview.Headers.Should().Equal("id", "name", "role");
        commaPreview.EstimatedTotalRows.Should().Be(3);
        commaPreview.PreviewRows.Should().HaveCount(3);
        commaPreview.PreviewRows[0].Should().Equal("1", "Alice", "Admin");

        // 2. Semicolon CSV
        var semiCsvPath = Path.Combine(_tempDir, "semi_sample.csv");
        await File.WriteAllTextAsync(semiCsvPath, "código;descrição;preço\n101;Café Arábica;15.50\n102;Chá Verde;8.00\n");

        var semiPreview = await _importService.PreviewFileAsync(semiCsvPath);
        semiPreview.DetectedDelimiter.Should().Be(';');
        semiPreview.Headers.Should().Equal("código", "descrição", "preço");
        semiPreview.EstimatedTotalRows.Should().Be(2);
        semiPreview.PreviewRows.Should().HaveCount(2);
        semiPreview.PreviewRows[0].Should().Equal("101", "Café Arábica", "15.50");
    }

    [Fact]
    public async Task PreviewFileAsync_WithJson_ArrayOfObjectsAndTabular()
    {
        // 1. Array of Objects
        var jsonObjectsPath = Path.Combine(_tempDir, "objects.json");
        var jsonObjectsContent = @"[
            { ""id"": 1, ""name"": ""Cheese"", ""active"": true },
            { ""id"": 2, ""name"": ""Wine"", ""active"": false }
        ]";
        await File.WriteAllTextAsync(jsonObjectsPath, jsonObjectsContent);

        var objectsPreview = await _importService.PreviewFileAsync(jsonObjectsPath);
        objectsPreview.Headers.Should().Equal("id", "name", "active");
        objectsPreview.EstimatedTotalRows.Should().Be(2);
        objectsPreview.PreviewRows[0][1].Should().Be("Cheese");
        objectsPreview.PreviewRows[0][2].Should().Be(true);

        // 2. Tabular format
        var jsonTabularPath = Path.Combine(_tempDir, "tabular.json");
        var jsonTabularContent = @"[
            [""colA"", ""colB""],
            [""val1"", ""val2""],
            [""val3"", ""val4""]
        ]";
        await File.WriteAllTextAsync(jsonTabularPath, jsonTabularContent);

        var tabularPreview = await _importService.PreviewFileAsync(jsonTabularPath);
        tabularPreview.Headers.Should().Equal("colA", "colB");
        tabularPreview.EstimatedTotalRows.Should().Be(2);
        tabularPreview.PreviewRows[0].Should().Equal("val1", "val2");
    }

    [Fact]
    public async Task PreviewFileAsync_WithExcel_ReadsColumnsAndRows()
    {
        var excelPath = Path.Combine(_tempDir, "sample.xlsx");
        var sampleRows = new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Product"] = "Monitor 4K", ["Qty"] = 10 },
            new Dictionary<string, object?> { ["Id"] = 2, ["Product"] = "Keyboard Mecânico", ["Qty"] = 25 }
        };

        await MiniExcel.SaveAsAsync(excelPath, sampleRows);

        var excelPreview = await _importService.PreviewFileAsync(excelPath);
        excelPreview.Headers.Should().Equal("Id", "Product", "Qty");
        excelPreview.EstimatedTotalRows.Should().Be(2);
        excelPreview.PreviewRows.Should().HaveCount(2);
        excelPreview.PreviewRows[0][1].Should().Be("Monitor 4K");
    }

    [Fact]
    public void AutoMapColumns_MatchesNormalizedAndExactNames()
    {
        var fileColumns = new List<string> { "user_id", "Full_Name", "E-MAIL", "random_note" };
        var targetColumns = new List<ColumnInfo>
        {
            new() { Name = "UserId", DataType = "INTEGER" },
            new() { Name = "FullName", DataType = "TEXT" },
            new() { Name = "Email", DataType = "TEXT" },
            new() { Name = "Department", DataType = "TEXT" }
        };

        var mappings = ColumnAutoMapper.AutoMapColumns(fileColumns, targetColumns);

        mappings.Should().HaveCount(4);
        mappings[0].SourceColumn.Should().Be("user_id");
        mappings[0].TargetColumn.Should().Be("UserId");
        mappings[0].IsIgnored.Should().BeFalse();

        mappings[1].SourceColumn.Should().Be("Full_Name");
        mappings[1].TargetColumn.Should().Be("FullName");
        mappings[1].IsIgnored.Should().BeFalse();

        mappings[2].SourceColumn.Should().Be("E-MAIL");
        mappings[2].TargetColumn.Should().Be("Email");
        mappings[2].IsIgnored.Should().BeFalse();

        mappings[3].SourceColumn.Should().Be("random_note");
        mappings[3].TargetColumn.Should().BeNull();
        mappings[3].IsIgnored.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteImportAsync_WithRollbackOnError_RollsBackOnSqlFailure()
    {
        var dbPath = Path.Combine(_tempDir, "test_rollback.db");
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = dbPath,
            Name = "TestRollbackDb"
        };

        using var dbService = new SqliteDatabaseService(config);
        await dbService.ConnectAsync();

        // Create table with UNIQUE constraint
        await dbService.ExecuteQueryAsync(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                sku TEXT UNIQUE NOT NULL,
                name TEXT NOT NULL
            );
        ");

        // CSV with duplicate SKU on row 3
        var csvPath = Path.Combine(_tempDir, "products_conflict.csv");
        await File.WriteAllTextAsync(csvPath,
            "sku,name\n" +
            "SKU-001,Widget Alpha\n" +
            "SKU-002,Widget Beta\n" +
            "SKU-001,Widget Duplicate\n" // Duplicate SKU -> should trigger failure
        );

        var importConfig = new ImportConfiguration
        {
            FilePath = csvPath,
            Format = ImportFileFormat.Csv,
            TargetTable = "products",
            ErrorStrategy = ImportErrorStrategy.RollbackOnError,
            BatchSize = 10,
            Mappings = new List<ColumnMapping>
            {
                new() { SourceColumn = "sku", TargetColumn = "sku" },
                new() { SourceColumn = "name", TargetColumn = "name" }
            }
        };

        var result = await _importService.ExecuteImportAsync(dbService, importConfig);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("revertida");
        result.Errors.Should().NotBeEmpty();

        // Check that NO rows were inserted due to rollback
        var verifyResult = await dbService.ExecuteQueryAsync("SELECT COUNT(*) FROM products;");
        verifyResult.Success.Should().BeTrue();
        Convert.ToInt32(verifyResult.Rows[0][0]).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteImportAsync_WithSkipErrorsAndContinue_InsertsValidRows()
    {
        var dbPath = Path.Combine(_tempDir, "test_skip.db");
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = dbPath,
            Name = "TestSkipDb"
        };

        using var dbService = new SqliteDatabaseService(config);
        await dbService.ConnectAsync();

        await dbService.ExecuteQueryAsync(@"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT UNIQUE NOT NULL,
                name TEXT NOT NULL
            );
        ");

        // CSV with 4 rows: row 1, 2, 4 are valid; row 3 duplicates row 1's code
        var csvPath = Path.Combine(_tempDir, "customers_skip.csv");
        await File.WriteAllTextAsync(csvPath,
            "code,name\n" +
            "C-01,Empresa Alpha\n" +
            "C-02,Empresa Beta\n" +
            "C-01,Empresa Duplicate\n" + // duplicate -> should be skipped
            "C-03,Empresa Gama\n"
        );

        var importConfig = new ImportConfiguration
        {
            FilePath = csvPath,
            Format = ImportFileFormat.Csv,
            TargetTable = "customers",
            ErrorStrategy = ImportErrorStrategy.SkipErrorsAndContinue,
            BatchSize = 10,
            Mappings = new List<ColumnMapping>
            {
                new() { SourceColumn = "code", TargetColumn = "code" },
                new() { SourceColumn = "name", TargetColumn = "name" }
            }
        };

        var result = await _importService.ExecuteImportAsync(dbService, importConfig);

        result.Success.Should().BeTrue();
        result.InsertedRowsCount.Should().Be(3);
        result.FailedRowsCount.Should().Be(1);
        result.Errors.Should().HaveCount(1);

        var verifyResult = await dbService.ExecuteQueryAsync("SELECT COUNT(*) FROM customers;");
        verifyResult.Success.Should().BeTrue();
        Convert.ToInt32(verifyResult.Rows[0][0]).Should().Be(3);
    }
}
