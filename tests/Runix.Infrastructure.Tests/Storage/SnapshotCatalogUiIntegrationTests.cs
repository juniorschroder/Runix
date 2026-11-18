using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff.Exceptions;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Storage;

public class SnapshotCatalogUiIntegrationTests : IDisposable
{
    private readonly string _testCatalogDir;
    private readonly SchemaSnapshotCatalogService _catalogService;
    private readonly SchemaSnapshotStorageService _storageService;

    public SnapshotCatalogUiIntegrationTests()
    {
        _testCatalogDir = Path.Combine(Path.GetTempPath(), "RunixUiIntegration_" + Guid.NewGuid().ToString("N"));
        _storageService = SchemaSnapshotStorageService.Instance;
        _catalogService = new SchemaSnapshotCatalogService(_storageService, _testCatalogDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testCatalogDir))
        {
            try { Directory.Delete(_testCatalogDir, true); } catch { }
        }
    }

    private static SchemaSnapshotModel CreateSnapshot(string dbName, string engine, string label, string notes = "")
    {
        return new SchemaSnapshotModel
        {
            DatabaseName = dbName,
            EngineType = engine,
            Label = label,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "users",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "email", DataType = "varchar(255)", IsNullable = false }
                    }
                }
            },
            Views = new List<ViewSnapshot>
            {
                new() { Name = "active_users", DefinitionDdl = "CREATE VIEW active_users AS SELECT * FROM users;" }
            },
            Triggers = new List<TriggerSnapshot>
            {
                new() { Name = "trg_audit", TargetTableName = "users", Timing = "AFTER", Event = "INSERT", DefinitionDdl = "CREATE TRIGGER trg_audit..." }
            }
        };
    }

    [Fact]
    public async Task CatalogFiltering_ByDatabaseNameLabelAndEngine_MatchesExpectedItems()
    {
        // Arrange
        var pgSnapshot = CreateSnapshot("ecommerce_prod", "PostgreSQL", "v1.0-release", "Release for client A");
        var sqliteSnapshot = CreateSnapshot("analytics_local", "SQLite", "dev-test", "Local dev database");
        var stagingSnapshot = CreateSnapshot("ecommerce_staging", "PostgreSQL", "pre-merge", "Staging environment");

        await _catalogService.SaveToCatalogAsync(pgSnapshot, pgSnapshot.Label, pgSnapshot.Notes);
        await _catalogService.SaveToCatalogAsync(sqliteSnapshot, sqliteSnapshot.Label, sqliteSnapshot.Notes);
        await _catalogService.SaveToCatalogAsync(stagingSnapshot, stagingSnapshot.Label, stagingSnapshot.Notes);

        // Act & Assert 1: Search by partial database name
        var dbSearchResults = await _catalogService.GetAllAsync("ecommerce");
        Assert.Equal(2, dbSearchResults.Count);
        Assert.All(dbSearchResults, item => Assert.Contains("ecommerce", item.DatabaseName, StringComparison.OrdinalIgnoreCase));

        // Act & Assert 2: Search by label
        var labelSearchResults = await _catalogService.GetAllAsync("v1.0-release");
        Assert.Single(labelSearchResults);
        Assert.Equal("ecommerce_prod", labelSearchResults[0].DatabaseName);

        // Act & Assert 3: Search by engine type
        var engineSearchResults = await _catalogService.GetAllAsync("SQLite");
        Assert.Single(engineSearchResults);
        Assert.Equal("analytics_local", engineSearchResults[0].DatabaseName);
        Assert.Equal("SQLite", engineSearchResults[0].EngineType);

        // Act & Assert 4: Empty query returns all items
        var allResults = await _catalogService.GetAllAsync(string.Empty);
        Assert.Equal(3, allResults.Count);
    }

    [Fact]
    public async Task CorruptedSnapshot_InCatalog_GeneratesIntegrityErrorItemWithoutCrashingAggregation()
    {
        // Arrange: 1 valid snapshot and 1 corrupted file
        var validSnapshot = CreateSnapshot("valid_db", "PostgreSQL", "valid-label");
        await _catalogService.SaveToCatalogAsync(validSnapshot, validSnapshot.Label);

        // Corrupted rschema (tampered payload or invalid bytes)
        var corruptedPath = Path.Combine(_testCatalogDir, "snapshot_corrupted_db_20261118_120000.rschema");
        await File.WriteAllBytesAsync(corruptedPath, new byte[] { 0x1F, 0x8B, 0x00, 0x01, 0xFF, 0xEE, 0xDD });

        // Act: load catalog
        var items = await _catalogService.GetAllAsync();

        // Assert: 2 items returned, catalog did not throw
        Assert.Equal(2, items.Count);

        var validItem = Assert.Single(items, i => i.DatabaseName == "valid_db");
        Assert.True(validItem.IsIntegrityValid);
        Assert.Null(validItem.IntegrityError);

        var corruptedItem = Assert.Single(items, i => i.FilePath == corruptedPath);
        Assert.False(corruptedItem.IsIntegrityValid);
        Assert.NotNull(corruptedItem.IntegrityError);
    }

    [Fact]
    public async Task LoadSnapshot_WhenIntegrityCompromised_ThrowsOrFlagsBasedOnTolerance()
    {
        // Arrange: Create a valid snapshot, save it, then tamper with the payload JSON
        var snapshot = CreateSnapshot("tamper_test_db", "PostgreSQL", "secure-baseline");
        var envelope = new SchemaSnapshotEnvelope
        {
            FormatVersion = 1,
            AppVersion = "1.4.0",
            ChecksumSha256 = _storageService.ComputeChecksum(snapshot),
            Snapshot = snapshot
        };

        // Tamper with the snapshot before serializing, so the checksumSha256 in envelope doesn't match
        snapshot.DatabaseName = "TAMPERED_DB_NAME";

        var filePath = Path.Combine(_testCatalogDir, "snapshot_tampered.rschema");
        using (var fs = File.Create(filePath))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: false))
        {
            await JsonSerializer.SerializeAsync(gz, envelope, SchemaSnapshotStorageService.CanonicalJsonOptions);
        }

        // Act & Assert 1: Standard loading throws SnapshotIntegrityException
        await Assert.ThrowsAsync<SnapshotIntegrityException>(async () =>
        {
            await _storageService.LoadSnapshotAsync(filePath, allowCompromised: false);
        });

        // Act & Assert 2: Force loading with allowCompromised = true succeeds and sets IsIntegrityCompromised = true
        var forceEnvelope = await _storageService.LoadSnapshotAsync(filePath, allowCompromised: true);
        Assert.NotNull(forceEnvelope);
        Assert.True(forceEnvelope.Snapshot.IsIntegrityCompromised);
        Assert.Equal("TAMPERED_DB_NAME", forceEnvelope.Snapshot.DatabaseName);
    }

    [Fact]
    public async Task SnapshotCatalogItem_FormattedMetadataPills_FormatCorrectly()
    {
        var snapshot = CreateSnapshot("metrics_db", "SQLite", "full-schema");
        string filePath = await _catalogService.SaveToCatalogAsync(snapshot, "full-schema", "Pill check");

        var item = await _catalogService.GetByFilePathAsync(filePath);
        Assert.NotNull(item);
        Assert.Equal(1, item.TableCount);
        Assert.Equal(1, item.ViewCount);
        Assert.Equal(1, item.TriggerCount);
        Assert.True(item.FileSizeBytes > 0);
        Assert.NotEmpty(item.FormattedFileSize);
        Assert.True(item.IsGzipCompressed);
    }
}
