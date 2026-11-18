using System.Collections.Generic;
using Runix.Core.Models;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff;
using Xunit;

namespace Runix.Infrastructure.Tests.Diff;

public class SyncDdlGeneratorSafeModeTests
{
    private static SchemaDiffResult CreateDiffWithRemovedTableAndColumn()
    {
        var src = new SchemaSnapshotModel
        {
            DatabaseName = "source_db",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "products",
                    Schema = "public",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true, IsNullable = false }
                    }
                }
            }
        };

        var tgt = new SchemaSnapshotModel
        {
            DatabaseName = "target_db",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "products",
                    Schema = "public",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true, IsNullable = false },
                        new() { Name = "deprecated_sku", DataType = "text", IsNullable = true }
                    }
                },
                new()
                {
                    Name = "obsolete_logs",
                    Schema = "public",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true }
                    }
                }
            }
        };

        return SchemaDiffEngine.Compare(src, tgt);
    }

    [Fact]
    public void SafeMode_Enabled_CommentsOutDestructiveStatements()
    {
        var diff = CreateDiffWithRemovedTableAndColumn();
        var options = new SyncOptions
        {
            TargetDialect = DatabaseType.PostgreSQL,
            SafeMode = true
        };

        var ddl = SyncDdlGenerator.Generate(diff, options);

        Assert.Contains("-- [SAFE MODE] DROP TABLE \"public\".\"obsolete_logs\";", ddl);
        Assert.Contains("-- [SAFE MODE] ALTER TABLE \"public\".\"products\" DROP COLUMN \"deprecated_sku\";", ddl);
        Assert.DoesNotContain("\nDROP TABLE \"public\".\"obsolete_logs\";", ddl);
    }

    [Fact]
    public void SafeMode_Disabled_EmitsActiveDestructiveStatements()
    {
        var diff = CreateDiffWithRemovedTableAndColumn();
        var options = new SyncOptions
        {
            TargetDialect = DatabaseType.PostgreSQL,
            SafeMode = false
        };

        var ddl = SyncDdlGenerator.Generate(diff, options);

        Assert.Contains("DROP TABLE \"public\".\"obsolete_logs\";", ddl);
        Assert.Contains("ALTER TABLE \"public\".\"products\" DROP COLUMN \"deprecated_sku\";", ddl);
        Assert.DoesNotContain("-- [SAFE MODE]", ddl);
    }
}
