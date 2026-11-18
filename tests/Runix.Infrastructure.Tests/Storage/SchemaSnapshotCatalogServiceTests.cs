using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Storage;

public class SchemaSnapshotCatalogServiceTests : IDisposable
{
    private readonly string _tempCatalogDir;
    private readonly SchemaSnapshotCatalogService _catalogService;

    public SchemaSnapshotCatalogServiceTests()
    {
        _tempCatalogDir = Path.Combine(Path.GetTempPath(), "RunixCatalogTest_" + Guid.NewGuid().ToString("N"));
        _catalogService = new SchemaSnapshotCatalogService(SchemaSnapshotStorageService.Instance, _tempCatalogDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempCatalogDir))
        {
            try { Directory.Delete(_tempCatalogDir, true); } catch { }
        }
    }

    private static SchemaSnapshotModel CreateTestSnapshot(string dbName = "sales_db", string label = "baseline-v1")
    {
        return new SchemaSnapshotModel
        {
            DatabaseName = dbName,
            EngineType = "PostgreSQL",
            Label = label,
            CreatedAt = DateTime.UtcNow,
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "products",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "int", IsPrimaryKey = true }
                    }
                }
            },
            Views = new List<ViewSnapshot>
            {
                new() { Name = "v_products", DefinitionDdl = "CREATE VIEW v_products AS SELECT * FROM products;" }
            }
        };
    }

    [Fact]
    public async Task SaveToCatalog_GeneratesValidRschemaFileInDirectory()
    {
        var snapshot = CreateTestSnapshot("finance_db", "q3-closing");

        string filePath = await _catalogService.SaveToCatalogAsync(snapshot, "q3-closing", "Closing snapshot notes");

        Assert.True(File.Exists(filePath));
        Assert.StartsWith(_tempCatalogDir, filePath);
        Assert.EndsWith(".rschema", filePath);
        Assert.Contains("finance_db", Path.GetFileName(filePath));

        var item = await _catalogService.GetByFilePathAsync(filePath);
        Assert.NotNull(item);
        Assert.Equal("q3-closing", item.Label);
        Assert.Equal("finance_db", item.DatabaseName);
        Assert.Equal(1, item.TableCount);
        Assert.Equal(1, item.ViewCount);
        Assert.True(item.IsIntegrityValid);
        Assert.True(item.IsGzipCompressed);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCatalogItemsSortedByDateDescending()
    {
        var snap1 = CreateTestSnapshot("db_first", "old-snap");
        snap1.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        var snap2 = CreateTestSnapshot("db_second", "new-snap");
        snap2.CreatedAt = DateTime.UtcNow;

        string file1 = await _catalogService.SaveToCatalogAsync(snap1, "old-snap");
        // Ensure slight time delay so file timestamp differs
        await Task.Delay(20);
        string file2 = await _catalogService.SaveToCatalogAsync(snap2, "new-snap");

        var items = await _catalogService.GetAllAsync();

        Assert.True(items.Count >= 2);
        Assert.Equal("new-snap", items[0].Label);
        Assert.Equal("old-snap", items[1].Label);
    }

    [Fact]
    public async Task GetAllAsync_WithSearchQuery_FiltersByLabelOrDatabaseName()
    {
        await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("crm_prod", "customer-migration"));
        await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("analytics_dw", "nightly-backup"));

        var filteredByLabel = await _catalogService.GetAllAsync("customer");
        Assert.Single(filteredByLabel);
        Assert.Equal("crm_prod", filteredByLabel[0].DatabaseName);

        var filteredByDb = await _catalogService.GetAllAsync("analytics");
        Assert.Single(filteredByDb);
        Assert.Equal("analytics_dw", filteredByDb[0].DatabaseName);

        var emptyResults = await _catalogService.GetAllAsync("non_existent_term");
        Assert.Empty(emptyResults);
    }

    [Fact]
    public async Task GetAllAsync_WithCorruptedFile_ReturnsItemWithIntegrityErrorWithoutCrashing()
    {
        // Valid file
        await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("healthy_db", "healthy"));

        // Corrupted file in directory
        string corruptedFile = Path.Combine(_tempCatalogDir, "corrupted_file.rschema");
        await File.WriteAllTextAsync(corruptedFile, "This is not valid json or gzip data");

        var items = await _catalogService.GetAllAsync();

        Assert.Equal(2, items.Count);
        var corruptedItem = Assert.Single(items, i => i.FilePath == corruptedFile);
        Assert.False(corruptedItem.IsIntegrityValid);
        Assert.NotNull(corruptedItem.IntegrityError);
    }

    [Fact]
    public async Task RenameAsync_UpdatesSnapshotLabelAndRecomputesChecksum()
    {
        string filePath = await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("inventory_db", "initial-label"));

        await _catalogService.RenameAsync(filePath, "renamed-production-v2");

        var item = await _catalogService.GetByFilePathAsync(filePath);
        Assert.NotNull(item);
        Assert.Equal("renamed-production-v2", item.Label);
        Assert.True(item.IsIntegrityValid);

        // Verify storage envelope directly
        var envelope = await SchemaSnapshotStorageService.Instance.LoadSnapshotAsync(filePath);
        Assert.Equal("renamed-production-v2", envelope.Metadata.Label);
        Assert.Equal("renamed-production-v2", envelope.Snapshot.Label);
        Assert.True(SchemaSnapshotStorageService.Instance.VerifyChecksum(envelope, out _));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileFromDisk()
    {
        string filePath = await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("temp_db", "to-delete"));
        Assert.True(File.Exists(filePath));

        await _catalogService.DeleteAsync(filePath);

        Assert.False(File.Exists(filePath));
        var item = await _catalogService.GetByFilePathAsync(filePath);
        Assert.Null(item);
    }

    [Fact]
    public async Task ExportToFileAsync_CopiesSnapshotToExternalDestination()
    {
        string sourcePath = await _catalogService.SaveToCatalogAsync(CreateTestSnapshot("export_source", "export-label"));
        string externalDir = Path.Combine(Path.GetTempPath(), "RunixExportTarget_" + Guid.NewGuid().ToString("N"));
        string destPath = Path.Combine(externalDir, "exported_backup.rschema");

        try
        {
            await _catalogService.ExportToFileAsync(sourcePath, destPath);

            Assert.True(File.Exists(destPath));
            var envelope = await SchemaSnapshotStorageService.Instance.LoadSnapshotAsync(destPath);
            Assert.Equal("export_source", envelope.Snapshot.DatabaseName);
            Assert.True(SchemaSnapshotStorageService.Instance.VerifyChecksum(envelope, out _));
        }
        finally
        {
            if (Directory.Exists(externalDir))
            {
                try { Directory.Delete(externalDir, true); } catch { }
            }
        }
    }
}
