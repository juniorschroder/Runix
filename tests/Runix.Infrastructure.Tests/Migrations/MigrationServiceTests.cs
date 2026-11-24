using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Models.Migrations;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Services;
using Xunit;

namespace Runix.Infrastructure.Tests.Migrations;

public class MigrationServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _tempMigrationsDir;
    private readonly MigrationService _service;

    public MigrationServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_mig_{Guid.NewGuid():N}.db");
        _tempMigrationsDir = Path.Combine(Path.GetTempPath(), $"runix_migdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempMigrationsDir);
        _service = new MigrationService();
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
        if (Directory.Exists(_tempMigrationsDir))
        {
            try { Directory.Delete(_tempMigrationsDir, true); } catch { }
        }
    }

    [Fact]
    public void ComputeNormalizedChecksum_NormalizesCrlfToLf()
    {
        string crlf = "CREATE TABLE test (\r\n  id INT PRIMARY KEY\r\n);\r\n";
        string lf = "CREATE TABLE test (\n  id INT PRIMARY KEY\n);\n";

        string hash1 = MigrationService.ComputeNormalizedChecksum(crlf);
        string hash2 = MigrationService.ComputeNormalizedChecksum(lf);

        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    [Fact]
    public async Task ScanAndApplyMigrations_FullLifecycle_Succeeds()
    {
        var config = new ConnectionConfig { Type = DatabaseType.SQLite, FilePath = _tempDbPath };
        using var db = new SqliteDatabaseService(config);
        await db.ConnectAsync();

        // 1. Create two migration files
        string file1 = Path.Combine(_tempMigrationsDir, "V1__create_users.sql");
        await File.WriteAllTextAsync(file1, "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);");

        string file2 = Path.Combine(_tempMigrationsDir, "V2__create_orders.sql");
        await File.WriteAllTextAsync(file2, "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL);");

        // 2. Scan directory: both should be Pending
        var scan1 = await _service.ScanMigrationsAsync(db, _tempMigrationsDir);
        Assert.Equal(2, scan1.Count);
        Assert.Equal("1", scan1[0].Version);
        Assert.Equal("create users", scan1[0].Description);
        Assert.Equal(MigrationStatus.Pending, scan1[0].Status);

        Assert.Equal("2", scan1[1].Version);
        Assert.Equal("create orders", scan1[1].Description);
        Assert.Equal(MigrationStatus.Pending, scan1[1].Status);

        // 3. Apply pending migrations
        var applyResult = await _service.ApplyPendingAsync(db, _tempMigrationsDir, scan1);
        Assert.True(applyResult.Success);
        Assert.Equal(2, applyResult.AppliedCount);
        Assert.Equal(2, applyResult.Reports.Count);

        // 4. Verify tables were created in the database
        var tables = await db.GetTablesAsync();
        Assert.Contains(tables, t => t.Name.Equals("users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tables, t => t.Name.Equals("orders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tables, t => t.Name.Equals("__runix_migrations", StringComparison.OrdinalIgnoreCase));

        // 5. Scan again: both should now be Applied
        var scan2 = await _service.ScanMigrationsAsync(db, _tempMigrationsDir);
        Assert.Equal(2, scan2.Count);
        Assert.Equal(MigrationStatus.Applied, scan2[0].Status);
        Assert.Equal(1, scan2[0].InstalledRank);
        Assert.Equal(MigrationStatus.Applied, scan2[1].Status);
        Assert.Equal(2, scan2[1].InstalledRank);

        // 6. Tamper Detection: modify V1 on disk
        await File.WriteAllTextAsync(file1, "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT);");

        var scan3 = await _service.ScanMigrationsAsync(db, _tempMigrationsDir);
        var tamperedItem = scan3.FirstOrDefault(m => m.Version == "1");
        Assert.NotNull(tamperedItem);
        Assert.Equal(MigrationStatus.MismatchChecksum, tamperedItem.Status);

        // 7. Verify ApplyPendingAsync blocks if tampered file exists
        string file3 = Path.Combine(_tempMigrationsDir, "V3__add_index.sql");
        await File.WriteAllTextAsync(file3, "CREATE INDEX idx_users_name ON users(name);");

        var blockedResult = await _service.ApplyPendingAsync(db, _tempMigrationsDir, scan3);
        Assert.False(blockedResult.Success);
        Assert.Contains("INTEGRITY CHECK FAILED", blockedResult.ErrorMessage);
    }
}
