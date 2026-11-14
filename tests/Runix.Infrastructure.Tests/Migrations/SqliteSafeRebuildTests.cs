using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Migrations;
using Xunit;

namespace Runix.Infrastructure.Tests.Migrations;

public class SqliteSafeRebuildTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseService _dbService;
    private readonly SchemaMigrationEngine _engine = new();

    public SqliteSafeRebuildTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"runix_migration_test_{Guid.NewGuid():N}.db");
        var config = new ConnectionConfig
        {
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

    [Fact]
    public async Task SafeRebuild_ShouldPreserveExistingData_WhenDroppingAndAddingColumns()
    {
        await _dbService.ConnectAsync();

        // 1. Arrange: Cria tabela inicial com dados
        var createSql = @"
CREATE TABLE inventory (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    stock INTEGER NOT NULL,
    old_notes TEXT
);

INSERT INTO inventory (id, name, stock, old_notes) VALUES
(1, 'Keyboard', 45, 'Batch 2024'),
(2, 'Monitor', 12, 'Warehouse B'),
(3, 'Headset', 80, 'Discontinued soon');
";
        var initResult = await _dbService.ExecuteQueryAsync(createSql);
        Assert.True(initResult.Success, initResult.ErrorMessage);

        // 2. Monta diff: remove old_notes, adiciona category com default
        var originalNode = new ErdTableNode
        {
            Table = new TableInfo { Name = "inventory" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT", IsNullable = false },
                new() { Name = "stock", DataType = "INTEGER", IsNullable = false },
                new() { Name = "old_notes", DataType = "TEXT" }
            }
        };

        var modifiedNode = new ErdTableNode
        {
            Table = new TableInfo { Name = "inventory" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "name", DataType = "TEXT", IsNullable = false },
                new() { Name = "stock", DataType = "INTEGER", IsNullable = false },
                new() { Name = "category", DataType = "TEXT", IsNullable = false, DefaultValue = "'General'" }
            }
        };

        var diff = TableSchemaDiff.Calculate(originalNode, modifiedNode);
        Assert.True(diff.HasChanges);
        Assert.Single(diff.RemovedColumns);
        Assert.Single(diff.AddedColumns);

        // 3. Act: Cria e executa o plano de migração
        var plan = _engine.CreatePlan(diff, DatabaseType.SQLite);
        Assert.True(plan.RequiresTableRebuild);

        var result = await _engine.ExecutePlanAsync(_dbService, plan);
        Assert.True(result.Success, result.ErrorMessage);

        // 4. Assert: Verifica se a tabela final possui as colunas esperadas e preservou todos os dados
        var checkData = await _dbService.ExecuteQueryAsync("SELECT id, name, stock, category FROM inventory ORDER BY id ASC;");
        Assert.True(checkData.Success, checkData.ErrorMessage);
        Assert.Equal(3, checkData.Rows.Count);

        Assert.Equal("1", checkData.Rows[0][0]?.ToString());
        Assert.Equal("Keyboard", checkData.Rows[0][1]?.ToString());
        Assert.Equal("45", checkData.Rows[0][2]?.ToString());
        Assert.Equal("General", checkData.Rows[0][3]?.ToString());

        Assert.Equal("2", checkData.Rows[1][0]?.ToString());
        Assert.Equal("Monitor", checkData.Rows[1][1]?.ToString());
        Assert.Equal("12", checkData.Rows[1][2]?.ToString());
        Assert.Equal("General", checkData.Rows[1][3]?.ToString());

        Assert.Equal("3", checkData.Rows[2][0]?.ToString());
        Assert.Equal("Headset", checkData.Rows[2][1]?.ToString());
        Assert.Equal("80", checkData.Rows[2][2]?.ToString());
        Assert.Equal("General", checkData.Rows[2][3]?.ToString());

        // Confirma que a coluna antiga 'old_notes' não existe mais
        var failQuery = await _dbService.ExecuteQueryAsync("SELECT old_notes FROM inventory;");
        Assert.False(failQuery.Success);
        Assert.Contains("no such column: old_notes", failQuery.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
