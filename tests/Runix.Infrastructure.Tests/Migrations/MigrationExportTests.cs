using System;
using System.IO;
using System.Threading.Tasks;
using Runix.Infrastructure.Services;
using Xunit;

namespace Runix.Infrastructure.Tests.Migrations;

public class MigrationExportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MigrationService _service;

    public MigrationExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"runix_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new MigrationService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExportDiffAsMigrationAsync_EmptyDirectory_CreatesV1InitialMigration()
    {
        string ddl = "CREATE TABLE users (\n    id INTEGER PRIMARY KEY,\n    username TEXT NOT NULL\n);";
        string filePath = await _service.ExportDiffAsMigrationAsync(_tempDir, "create_users_table", ddl);

        Assert.True(File.Exists(filePath));
        Assert.Equal("V1__create_users_table.sql", Path.GetFileName(filePath));

        string content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("Version: 1", content);
        Assert.Contains("Description: create_users_table", content);
        Assert.Contains(ddl, content);
    }

    [Fact]
    public async Task ExportDiffAsMigrationAsync_ExistingMigrations_IncrementsHighestVersion()
    {
        // Pre-create V1 and V2
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "V1__initial.sql"), "CREATE TABLE base (id INT);");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "V2__add_status.sql"), "ALTER TABLE base ADD COLUMN status TEXT;");

        string ddl = "CREATE TABLE orders (id INTEGER PRIMARY KEY);";
        string filePath = await _service.ExportDiffAsMigrationAsync(_tempDir, "create_orders", ddl);

        Assert.True(File.Exists(filePath));
        Assert.Equal("V3__create_orders.sql", Path.GetFileName(filePath));

        string content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("Version: 3", content);
        Assert.Contains("Description: create_orders", content);
        Assert.Contains("CREATE TABLE orders", content);
    }

    [Fact]
    public async Task ExportDiffAsMigrationAsync_SanitizesDescriptionAndHandlesEmpty()
    {
        string ddl = "CREATE INDEX idx_user ON users(email);";
        string filePath = await _service.ExportDiffAsMigrationAsync(_tempDir, "Add user email - index & unique!", ddl);

        Assert.True(File.Exists(filePath));
        Assert.Equal("V1__Add_user_email_index_unique.sql", Path.GetFileName(filePath));

        // Test with empty description -> fallback to schema_sync
        string filePath2 = await _service.ExportDiffAsMigrationAsync(_tempDir, "   ", ddl);
        Assert.True(File.Exists(filePath2));
        Assert.Equal("V2__schema_sync.sql", Path.GetFileName(filePath2));
    }

    [Fact]
    public async Task ExportDiffAsMigrationAsync_ThrowsOnNullOrEmptyDirectory()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ExportDiffAsMigrationAsync(string.Empty, "desc", "SELECT 1;"));
    }
}
