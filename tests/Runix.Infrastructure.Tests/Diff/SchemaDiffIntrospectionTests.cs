using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Diff;
using Xunit;

namespace Runix.Infrastructure.Tests.Diff;

public class SchemaDiffIntrospectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseService _dbService;
    private readonly SchemaDiffService _diffService = SchemaDiffService.Instance;

    public SchemaDiffIntrospectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_diff_test_{Guid.NewGuid():N}.db");
        var config = new ConnectionConfig
        {
            Name = "Diff Introspection Test DB",
            Type = DatabaseType.SQLite,
            FilePath = _dbPath
        };
        _dbService = new SqliteDatabaseService(config);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private async Task SeedDatabaseAsync()
    {
        await _dbService.ConnectAsync();

        string setupSql = @"
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                total NUMERIC NOT NULL,
                status TEXT NOT NULL,
                CONSTRAINT chk_orders_total CHECK (total >= 0)
            );

            CREATE TABLE order_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                FOREIGN KEY (order_id) REFERENCES orders(id),
                CHECK (quantity > 0)
            );

            CREATE VIEW v_active_orders AS
            SELECT id, total FROM orders WHERE status = 'active';

            CREATE TRIGGER trg_orders_after_update
            AFTER UPDATE ON orders
            BEGIN
                SELECT 1;
            END;
        ";

        var res = await _dbService.ExecuteQueryAsync(setupSql);
        Assert.True(res.Success, res.ErrorMessage);
    }

    [Fact]
    public async Task ExtractSnapshotAsync_FullExtraction_ExtractsTablesViewsTriggersAndChecks()
    {
        await SeedDatabaseAsync();

        var snapshot = await _diffService.ExtractSnapshotAsync(_dbService);

        Assert.NotNull(snapshot);
        Assert.Equal("SQLite", snapshot.EngineType);

        // Verify tables
        var ordersTable = Assert.Single(snapshot.Tables, t => t.Name == "orders");
        var itemsTable = Assert.Single(snapshot.Tables, t => t.Name == "order_items");

        // Verify foreign key
        var fk = Assert.Single(itemsTable.ForeignKeys);
        Assert.Equal("order_id", fk.ColumnName);
        Assert.Equal("orders", fk.ForeignTableName);

        // Verify Check Constraints
        var orderCheck = Assert.Single(ordersTable.CheckConstraints);
        Assert.Equal("chk_orders_total", orderCheck.Name);
        Assert.Contains("total >= 0", orderCheck.Expression);

        var itemCheck = Assert.Single(itemsTable.CheckConstraints);
        Assert.Contains("quantity > 0", itemCheck.Expression);

        // Verify Views
        var view = Assert.Single(snapshot.Views);
        Assert.Equal("v_active_orders", view.Name);
        Assert.Contains("v_active_orders", view.DefinitionDdl);

        // Verify Triggers
        var trigger = Assert.Single(snapshot.Triggers);
        Assert.Equal("trg_orders_after_update", trigger.Name);
        Assert.Equal("orders", trigger.TargetTableName);
        Assert.Equal("AFTER", trigger.Timing);
        Assert.Equal("UPDATE", trigger.Event);
        Assert.Contains("trg_orders_after_update", trigger.DefinitionDdl);
    }

    [Fact]
    public async Task ExtractSnapshotAsync_WithDisabledOptions_SelectivelyOmitsElements()
    {
        await SeedDatabaseAsync();

        var options = new SnapshotExtractionOptions
        {
            IncludeTables = true,
            IncludeForeignKeys = false,
            IncludeIndexes = false,
            IncludeCheckConstraints = false,
            IncludeViews = false,
            IncludeTriggers = false
        };

        var snapshot = await _diffService.ExtractSnapshotAsync(_dbService, options);

        Assert.NotEmpty(snapshot.Tables);
        var ordersTable = snapshot.Tables.First(t => t.Name == "orders");
        var itemsTable = snapshot.Tables.First(t => t.Name == "order_items");

        Assert.Empty(ordersTable.CheckConstraints);
        Assert.Empty(itemsTable.CheckConstraints);
        Assert.Empty(itemsTable.ForeignKeys);
        Assert.Empty(snapshot.Views);
        Assert.Empty(snapshot.Triggers);
    }

    [Fact]
    public async Task ExtractSnapshotAsync_WithOmittedTables_ExtractsOnlyViewsAndTriggers()
    {
        await SeedDatabaseAsync();

        var options = new SnapshotExtractionOptions
        {
            IncludeTables = false,
            IncludeViews = true,
            IncludeTriggers = true
        };

        var snapshot = await _diffService.ExtractSnapshotAsync(_dbService, options);

        Assert.Empty(snapshot.Tables);
        Assert.Single(snapshot.Views);
        Assert.Single(snapshot.Triggers);
        Assert.Equal("v_active_orders", snapshot.Views[0].Name);
        Assert.Equal("trg_orders_after_update", snapshot.Triggers[0].Name);
    }
}
