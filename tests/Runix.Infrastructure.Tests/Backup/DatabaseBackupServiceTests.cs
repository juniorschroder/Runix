using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Backup;
using Runix.Infrastructure.Data;
using Xunit;

namespace Runix.Infrastructure.Tests.Backup;

public class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _sourceDbPath;
    private readonly string _targetDbPath;
    private readonly SqliteDatabaseService _sourceDb;
    private readonly SqliteDatabaseService _targetDb;

    public DatabaseBackupServiceTests()
    {
        _sourceDbPath = Path.Combine(Path.GetTempPath(), $"runix_bkup_src_{Guid.NewGuid():N}.db");
        _targetDbPath = Path.Combine(Path.GetTempPath(), $"runix_bkup_dst_{Guid.NewGuid():N}.db");

        _sourceDb = new SqliteDatabaseService(new ConnectionConfig
        {
            Name = "SourceDb",
            Type = DatabaseType.SQLite,
            FilePath = _sourceDbPath
        });
        _sourceDb.ConnectAsync().GetAwaiter().GetResult();

        _targetDb = new SqliteDatabaseService(new ConnectionConfig
        {
            Name = "TargetDb",
            Type = DatabaseType.SQLite,
            FilePath = _targetDbPath
        });
        _targetDb.ConnectAsync().GetAwaiter().GetResult();

        _sourceDb.ExecuteQueryAsync(@"
            CREATE TABLE categories (id INTEGER PRIMARY KEY, title TEXT NOT NULL);
            CREATE TABLE products (id INTEGER PRIMARY KEY, category_id INTEGER, name TEXT NOT NULL, price REAL, FOREIGN KEY(category_id) REFERENCES categories(id));
            INSERT INTO categories (id, title) VALUES (1, 'Artisan Cheeses'), (2, 'Wines');
            INSERT INTO products (id, category_id, name, price) VALUES (10, 1, 'Roquefort', 24.5), (11, 1, 'Brie; Aged', 18.0);
            CREATE VIEW v_cheese_products AS SELECT id, name, price FROM products WHERE category_id = 1;
            CREATE TRIGGER trg_prod_insert AFTER INSERT ON products BEGIN SELECT 1; SELECT 2; END;
        ").GetAwaiter().GetResult();
    }

    [Fact]
    public void SortTablesTopologically_OrdersParentTablesBeforeChildTables()
    {
        var backupService = new DatabaseBackupService(_sourceDb);
        var tables = new[] { "order_items", "orders", "customers" };
        var fks = new List<ForeignKeyInfo>
        {
            new() { SourceTable = "orders", TargetTable = "customers" },
            new() { SourceTable = "order_items", TargetTable = "orders" }
        };

        var ordered = backupService.SortTablesTopologically(tables, fks);
        Assert.Equal(new[] { "customers", "orders", "order_items" }, ordered);
    }

    [Fact]
    public void SplitSqlStatements_PreservesSemicolonsInsideStringsTriggersAndDollarBlocks()
    {
        var backupService = new DatabaseBackupService(_sourceDb);
        var script = @"
            -- Header comment with ; semicolon
            INSERT INTO products (id, name) VALUES (1, 'Brie; Aged');
            CREATE TRIGGER trg_test AFTER INSERT ON products BEGIN
                INSERT INTO log VALUES ('a;b');
                SELECT 1;
            END;
            CREATE OR REPLACE FUNCTION fn_pg() RETURNS void AS $$
            BEGIN
                PERFORM 1;
                PERFORM 2;
            END;
            $$ LANGUAGE plpgsql;";

        var stmts = backupService.SplitSqlStatements(script);
        Assert.Equal(3, stmts.Count);
        Assert.Contains("'Brie; Aged'", stmts[0]);
        Assert.Contains("CREATE TRIGGER trg_test", stmts[1]);
        Assert.Contains("$$", stmts[2]);
    }

    [Fact]
    public async Task CreateAndRestoreCompressedSqlGzipBackup_RestoresFullSchemaViewsTriggersAndData()
    {
        var gzPath = Path.Combine(Path.GetTempPath(), $"runix_backup_{Guid.NewGuid():N}.sql.gz");
        try
        {
            var sourceBackupSvc = new DatabaseBackupService(_sourceDb);
            var backupRes = await sourceBackupSvc.CreateBackupAsync(new BackupOptions
            {
                OutputFilePath = gzPath,
                Format = BackupFormat.CompressedSqlGzip,
                Scope = BackupScope.Full,
                IncludeDropStatements = true,
                IncludeProgrammableObjects = true
            });

            Assert.True(backupRes.Success, backupRes.ErrorMessage);
            Assert.Equal(2, backupRes.TablesProcessed);
            Assert.Equal(4, backupRes.RowsProcessed);
            Assert.True(File.Exists(gzPath));

            var targetBackupSvc = new DatabaseBackupService(_targetDb);
            var restoreRes = await targetBackupSvc.RestoreBackupAsync(new RestoreOptions
            {
                InputFilePath = gzPath
            });

            Assert.True(restoreRes.Success, restoreRes.ErrorMessage);

            var checkView = await _targetDb.ExecuteQueryAsync("SELECT name FROM v_cheese_products ORDER BY id;");
            Assert.True(checkView.Success, checkView.ErrorMessage);
            Assert.Equal(2, checkView.Rows.Count);
            Assert.Equal("Brie; Aged", checkView.Rows[1][0]?.ToString());
        }
        finally
        {
            if (File.Exists(gzPath)) File.Delete(gzPath);
        }
    }

    [Fact]
    public async Task CreateAndRestoreSqliteBinaryBackup_CopiesDatabaseOnline()
    {
        var binPath = Path.Combine(Path.GetTempPath(), $"runix_bin_backup_{Guid.NewGuid():N}.db");
        try
        {
            var sourceBackupSvc = new DatabaseBackupService(_sourceDb);
            var backupRes = await sourceBackupSvc.CreateBackupAsync(new BackupOptions
            {
                OutputFilePath = binPath,
                Format = BackupFormat.SqliteBinary
            });

            Assert.True(backupRes.Success, backupRes.ErrorMessage);
            Assert.True(File.Exists(binPath));

            var targetBackupSvc = new DatabaseBackupService(_targetDb);
            var restoreRes = await targetBackupSvc.RestoreBackupAsync(new RestoreOptions
            {
                InputFilePath = binPath
            });

            Assert.True(restoreRes.Success, restoreRes.ErrorMessage);

            var checkRows = await _targetDb.ExecuteQueryAsync("SELECT COUNT(*) FROM products;");
            Assert.True(checkRows.Success);
            Assert.Equal(2L, Convert.ToInt64(checkRows.Rows[0][0]));
        }
        finally
        {
            if (File.Exists(binPath)) File.Delete(binPath);
        }
    }

    public void Dispose()
    {
        _sourceDb.Dispose();
        _targetDb.Dispose();
        try { if (File.Exists(_sourceDbPath)) File.Delete(_sourceDbPath); } catch { }
        try { if (File.Exists(_targetDbPath)) File.Delete(_targetDbPath); } catch { }
    }
}
