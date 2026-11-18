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

public class SchemaSnapshotStorageServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly SchemaSnapshotStorageService _service = SchemaSnapshotStorageService.Instance;

    public SchemaSnapshotStorageServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "RunixTestSnapshots_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private static SchemaSnapshotModel CreateSampleSnapshot()
    {
        return new SchemaSnapshotModel
        {
            DatabaseName = "app_db",
            EngineType = "PostgreSQL",
            Label = "v1-init",
            Notes = "Initial schema baseline",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "customers",
                    Schema = "public",
                    Comment = "Customer entities",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true, IsNullable = false },
                        new() { Name = "name", DataType = "varchar(100)", IsNullable = false },
                        new() { Name = "age", DataType = "integer", IsNullable = true }
                    },
                    Indexes = new List<IndexSnapshot>
                    {
                        new() { Name = "idx_customers_name", Columns = new List<string> { "name" } }
                    },
                    CheckConstraints = new List<CheckConstraintSnapshot>
                    {
                        new() { Name = "chk_customers_age", Expression = "age >= 18", ColumnName = "age" }
                    }
                }
            },
            Views = new List<ViewSnapshot>
            {
                new()
                {
                    Name = "adult_customers",
                    Schema = "public",
                    DefinitionDdl = "CREATE VIEW adult_customers AS SELECT * FROM customers WHERE age >= 18;"
                }
            },
            Triggers = new List<TriggerSnapshot>
            {
                new()
                {
                    Name = "trg_log_insert",
                    Schema = "public",
                    TargetTableName = "customers",
                    Timing = "AFTER",
                    Event = "INSERT",
                    DefinitionDdl = "CREATE TRIGGER trg_log_insert AFTER INSERT ON customers FOR EACH ROW EXECUTE PROCEDURE log_insert();"
                }
            }
        };
    }

    [Fact]
    public async Task SaveAndLoad_GZipCompressed_RoundtripPreservesFullSchema()
    {
        string filePath = Path.Combine(_testDir, "test_compressed.rschema");
        var original = CreateSampleSnapshot();

        await _service.SaveSnapshotAsync(filePath, original, compressGzip: true);

        Assert.True(File.Exists(filePath));
        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length >= 2);
        Assert.Equal(0x1F, bytes[0]);
        Assert.Equal(0x8B, bytes[1]);

        var loadedEnvelope = await _service.LoadSnapshotAsync(filePath);

        Assert.NotNull(loadedEnvelope);
        Assert.NotNull(loadedEnvelope.Snapshot);
        Assert.Equal("app_db", loadedEnvelope.Snapshot.DatabaseName);
        Assert.Equal("v1-init", loadedEnvelope.Snapshot.Label);
        Assert.Single(loadedEnvelope.Snapshot.Tables);
        Assert.Single(loadedEnvelope.Snapshot.Views);
        Assert.Single(loadedEnvelope.Snapshot.Triggers);
        Assert.Equal("adult_customers", loadedEnvelope.Snapshot.Views[0].Name);
        Assert.Equal("trg_log_insert", loadedEnvelope.Snapshot.Triggers[0].Name);
        Assert.Single(loadedEnvelope.Snapshot.Tables[0].CheckConstraints);
        Assert.Equal("chk_customers_age", loadedEnvelope.Snapshot.Tables[0].CheckConstraints[0].Name);

        Assert.False(loadedEnvelope.Snapshot.IsIntegrityCompromised);
        Assert.Equal(1, loadedEnvelope.Metadata.TableCount);
        Assert.Equal(1, loadedEnvelope.Metadata.ViewCount);
        Assert.Equal(1, loadedEnvelope.Metadata.TriggerCount);
    }

    [Fact]
    public async Task SaveAndLoad_UncompressedJson_RoundtripPreservesFullSchema()
    {
        string filePath = Path.Combine(_testDir, "test_uncompressed.json");
        var original = CreateSampleSnapshot();

        await _service.SaveSnapshotAsync(filePath, original, compressGzip: false);

        Assert.True(File.Exists(filePath));
        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        // Should start with '{' (0x7B)
        Assert.Equal((byte)'{', bytes[0]);

        var loadedEnvelope = await _service.LoadSnapshotAsync(filePath);

        Assert.NotNull(loadedEnvelope);
        Assert.Equal("app_db", loadedEnvelope.Snapshot.DatabaseName);
        Assert.Single(loadedEnvelope.Snapshot.Tables);
        Assert.False(loadedEnvelope.Snapshot.IsIntegrityCompromised);
    }

    [Fact]
    public async Task LoadSnapshot_DetectsGZipMagicHeaderTransparently()
    {
        string gzipPath = Path.Combine(_testDir, "auto_detect.rschema");
        string jsonPath = Path.Combine(_testDir, "auto_detect.json");
        var original = CreateSampleSnapshot();

        await _service.SaveSnapshotAsync(gzipPath, original, compressGzip: true);
        await _service.SaveSnapshotAsync(jsonPath, original, compressGzip: false);

        var fromGzip = await _service.LoadSnapshotAsync(gzipPath);
        var fromJson = await _service.LoadSnapshotAsync(jsonPath);

        Assert.Equal(fromGzip.ChecksumSha256, fromJson.ChecksumSha256);
        Assert.Equal(fromGzip.Snapshot.DatabaseName, fromJson.Snapshot.DatabaseName);
    }

    [Fact]
    public async Task LoadSnapshot_TamperedPayload_ThrowsSnapshotIntegrityException()
    {
        string filePath = Path.Combine(_testDir, "tampered.rschema");
        var original = CreateSampleSnapshot();

        await _service.SaveSnapshotAsync(filePath, original, compressGzip: false);

        // Tamper with the file: change a table name in the JSON without updating ChecksumSha256
        string json = await File.ReadAllTextAsync(filePath);
        string tamperedJson = json.Replace("customers", "tampered_customers");
        await File.WriteAllTextAsync(filePath, tamperedJson);

        var ex = await Assert.ThrowsAsync<SnapshotIntegrityException>(() =>
            _service.LoadSnapshotAsync(filePath, allowCompromised: false));

        Assert.NotNull(ex.ExpectedHash);
        Assert.NotNull(ex.ActualHash);
        Assert.NotEqual(ex.ExpectedHash, ex.ActualHash);
        Assert.Equal(filePath, ex.FilePath);
    }

    [Fact]
    public async Task LoadSnapshot_TamperedPayload_WhenAllowCompromisedIsTrue_SetsIsIntegrityCompromisedTrue()
    {
        string filePath = Path.Combine(_testDir, "tampered_allowed.json");
        var original = CreateSampleSnapshot();

        await _service.SaveSnapshotAsync(filePath, original, compressGzip: false);

        string json = await File.ReadAllTextAsync(filePath);
        string tamperedJson = json.Replace("customers", "compromised_customers");
        await File.WriteAllTextAsync(filePath, tamperedJson);

        var loadedEnvelope = await _service.LoadSnapshotAsync(filePath, allowCompromised: true);

        Assert.NotNull(loadedEnvelope);
        Assert.True(loadedEnvelope.Snapshot.IsIntegrityCompromised);
        Assert.Equal("compromised_customers", loadedEnvelope.Snapshot.Tables[0].Name);
    }

    [Fact]
    public async Task SaveSnapshot_AtomicWrite_CleansUpTempFileOnError()
    {
        string targetFilePath = Path.Combine(_testDir, "sub_dir", "final.rschema");
        var snapshot = CreateSampleSnapshot();

        // Pass a cancellation token already canceled to simulate interruption
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.SaveSnapshotAsync(targetFilePath, snapshot, compressGzip: true, cancellationToken: cts.Token));

        Assert.False(File.Exists(targetFilePath));
        var tempFiles = Directory.GetFiles(_testDir, "*.tmp.*", SearchOption.AllDirectories);
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void ComputeChecksum_IsDeterministicAcrossIdenticalSnapshots()
    {
        var fixedDate = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        var snap1 = CreateSampleSnapshot();
        snap1.CreatedAt = fixedDate;
        var snap2 = CreateSampleSnapshot();
        snap2.CreatedAt = fixedDate;

        string hash1 = _service.ComputeChecksum(snap1);
        string hash2 = _service.ComputeChecksum(snap2);

        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);

        // Modifying snapshot changes hash
        snap2.Tables[0].Name = "modified_table";
        string hash3 = _service.ComputeChecksum(snap2);
        Assert.NotEqual(hash1, hash3);
    }
}
