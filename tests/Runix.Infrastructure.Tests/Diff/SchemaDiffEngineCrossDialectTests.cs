using System.Collections.Generic;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff;
using Xunit;

namespace Runix.Infrastructure.Tests.Diff;

public class SchemaDiffEngineCrossDialectTests
{
    private static SchemaSnapshotModel CreateSqliteSnapshot()
    {
        return new SchemaSnapshotModel
        {
            DatabaseName = "sqlite_app.db",
            EngineType = "SQLite",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "users",
                    Schema = "main",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true, IsNullable = false },
                        new() { Name = "name", DataType = "TEXT", IsNullable = false },
                        new() { Name = "balance", DataType = "NUMERIC", IsNullable = true },
                        new() { Name = "is_active", DataType = "INTEGER", IsNullable = false }
                    }
                }
            }
        };
    }

    private static SchemaSnapshotModel CreatePostgresSnapshot()
    {
        return new SchemaSnapshotModel
        {
            DatabaseName = "pg_prod",
            EngineType = "PostgreSQL",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "users",
                    Schema = "public",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "bigint", IsPrimaryKey = true, IsNullable = false },
                        new() { Name = "name", DataType = "varchar(255)", IsNullable = false },
                        new() { Name = "balance", DataType = "decimal(10,2)", IsNullable = true },
                        new() { Name = "is_active", DataType = "boolean", IsNullable = false }
                    }
                }
            }
        };
    }

    [Fact]
    public void TolerantMode_CrossDialectAffinities_TreatedAsCompatible()
    {
        var src = CreateSqliteSnapshot();
        var tgt = CreatePostgresSnapshot();

        var result = SchemaDiffEngine.Compare(src, tgt, new SchemaCompareOptions { StrictTypeMatching = false });

        Assert.Single(result.Tables);
        var table = result.Tables[0];

        // All 4 columns should be marked cross-dialect compatible and table status Identical
        Assert.Equal(DiffStatus.Identical, table.Status);

        var idCol = table.ColumnDiffs.Find(c => c.ColumnName == "id");
        Assert.NotNull(idCol);
        Assert.True(idCol.IsCrossDialectCompatible);
        Assert.Contains("INTEGER ↔ bigint", idCol.DialectNote);

        var nameCol = table.ColumnDiffs.Find(c => c.ColumnName == "name");
        Assert.NotNull(nameCol);
        Assert.True(nameCol.IsCrossDialectCompatible);

        var balanceCol = table.ColumnDiffs.Find(c => c.ColumnName == "balance");
        Assert.NotNull(balanceCol);
        Assert.True(balanceCol.IsCrossDialectCompatible);

        var activeCol = table.ColumnDiffs.Find(c => c.ColumnName == "is_active");
        Assert.NotNull(activeCol);
        Assert.True(activeCol.IsCrossDialectCompatible);
    }

    [Fact]
    public void StrictMode_CrossDialectDifferences_ReportedAsModified()
    {
        var src = CreateSqliteSnapshot();
        var tgt = CreatePostgresSnapshot();

        var result = SchemaDiffEngine.Compare(src, tgt, new SchemaCompareOptions { StrictTypeMatching = true });

        Assert.Single(result.Tables);
        var table = result.Tables[0];

        // In strict mode, differences between "integer" and "bigint", "numeric" and "decimal" are reported as Modified
        Assert.Equal(DiffStatus.Modified, table.Status);

        var idCol = table.ColumnDiffs.Find(c => c.ColumnName == "id");
        Assert.NotNull(idCol);
        Assert.Equal(DiffStatus.Modified, idCol.Status);
        Assert.False(idCol.IsCrossDialectCompatible);
    }

    [Fact]
    public void FourWayCombinations_InvertDirection_SwapsAddedAndRemoved()
    {
        var emptySnapshot = new SchemaSnapshotModel { DatabaseName = "empty", Tables = new List<TableSnapshot>() };
        var populatedSnapshot = CreateSqliteSnapshot();

        // 1. Populated -> Empty (Table is Added)
        var diffNormal = SchemaDiffEngine.Compare(populatedSnapshot, emptySnapshot, invertDirection: false);
        Assert.Single(diffNormal.Tables);
        Assert.Equal(DiffStatus.Added, diffNormal.Tables[0].Status);

        // 2. Inverted Direction (Populated -> Empty with Invert = Table is Removed)
        var diffInverted = SchemaDiffEngine.Compare(populatedSnapshot, emptySnapshot, invertDirection: true);
        Assert.Single(diffInverted.Tables);
        Assert.Equal(DiffStatus.Removed, diffInverted.Tables[0].Status);

        // 3. Snapshot -> Snapshot symmetrical check
        var diffSelf = SchemaDiffEngine.Compare(populatedSnapshot, populatedSnapshot);
        Assert.Single(diffSelf.Tables);
        Assert.Equal(DiffStatus.Identical, diffSelf.Tables[0].Status);
    }
}
