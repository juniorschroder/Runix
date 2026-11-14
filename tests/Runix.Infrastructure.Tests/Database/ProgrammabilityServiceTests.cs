using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;
using Xunit;

namespace Runix.Infrastructure.Tests.Database;

public class ProgrammabilityServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _dbService;
    private readonly ProgrammabilityService _progService;

    public ProgrammabilityServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_prog_test_{Guid.NewGuid():N}.db");
        _dbService = new SqliteDatabaseService(new ConnectionConfig
        {
            Name = "ProgTestDb",
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        });
        _dbService.ConnectAsync().GetAwaiter().GetResult();

        _dbService.ExecuteQueryAsync(@"
            CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, active INTEGER DEFAULT 1);
            CREATE TABLE audit_log (id INTEGER PRIMARY KEY, action TEXT);
            CREATE VIEW v_active_customers AS SELECT id, name FROM customers WHERE active = 1;
            CREATE TRIGGER trg_customer_insert AFTER INSERT ON customers BEGIN INSERT INTO audit_log(action) VALUES ('inserted'); END;
            CREATE INDEX idx_customers_name ON customers(name);
        ").GetAwaiter().GetResult();

        _progService = new ProgrammabilityService(_dbService);
    }

    [Fact]
    public async Task GetProgrammableObjectsAsync_ReturnsViewsTriggersAndIndexes()
    {
        var objects = await _progService.GetProgrammableObjectsAsync();

        Assert.Contains(objects, o => o.ObjectType == DatabaseObjectType.View && o.Name == "v_active_customers");
        Assert.Contains(objects, o => o.ObjectType == DatabaseObjectType.Trigger && o.Name == "trg_customer_insert" && o.TargetTableName == "customers");
        Assert.Contains(objects, o => o.ObjectType == DatabaseObjectType.Index && o.Name == "idx_customers_name" && o.TargetTableName == "customers");
    }

    [Fact]
    public async Task ApplyObjectDdlAsync_ReplacesViewAndRollsBackOnSyntaxError()
    {
        var view = (await _progService.GetProgrammableObjectsAsync(DatabaseObjectType.View)).Single(v => v.Name == "v_active_customers");

        // 1. Submeter DDL com erro de sintaxe: transação deve fazer rollback e manter a view original!
        var invalidResult = await _progService.ApplyObjectDdlAsync(view, "CREATE VIEW v_active_customers AS SELECT FROM WHERE INVALID SYNTAX;");
        Assert.False(invalidResult.Success);

        var stillExists = await _progService.GetProgrammableObjectsAsync(DatabaseObjectType.View);
        Assert.Single(stillExists, v => v.Name == "v_active_customers");

        // 2. Submeter novo DDL válido (inclusive com CREATE OR REPLACE que é normalizado no SQLite)
        var validResult = await _progService.ApplyObjectDdlAsync(view, "CREATE OR REPLACE VIEW v_active_customers AS SELECT id FROM customers WHERE active = 1;");
        Assert.True(validResult.Success, validResult.ErrorMessage);

        var updatedDdl = await _progService.GetObjectDdlAsync(new DatabaseObjectInfo { Name = "v_active_customers", ObjectType = DatabaseObjectType.View });
        Assert.Contains("SELECT id FROM customers", updatedDdl);
    }

    [Fact]
    public async Task DropObjectAsync_RemovesObjectFromDatabase()
    {
        var idx = (await _progService.GetProgrammableObjectsAsync(DatabaseObjectType.Index)).Single(i => i.Name == "idx_customers_name");
        var dropRes = await _progService.DropObjectAsync(idx);
        Assert.True(dropRes.Success, dropRes.ErrorMessage);

        var remaining = await _progService.GetProgrammableObjectsAsync(DatabaseObjectType.Index);
        Assert.DoesNotContain(remaining, i => i.Name == "idx_customers_name");
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }
}
