using System;
using System.Collections.Generic;
using Runix.Core.Models;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff;
using Xunit;

namespace Runix.Infrastructure.Tests.Diff;

public class SchemaDiffServiceTests
{
    private readonly SchemaDiffService _service = SchemaDiffService.Instance;

    [Fact]
    public void SnapshotSerialization_RoundTripPreservesAllMetadata()
    {
        var snapshot = new SchemaSnapshotModel
        {
            Version = 1,
            DatabaseName = "ecommerce",
            EngineType = "PostgreSQL",
            CreatedAt = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc),
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "orders",
                    Schema = "public",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true, IsNullable = false },
                        new() { Name = "total", DataType = "numeric", DefaultValue = "0.0", IsNullable = false }
                    },
                    Indexes = new List<IndexSnapshot>
                    {
                        new() { Name = "idx_orders_total", IsUnique = false, Columns = new List<string> { "total" } }
                    },
                    ForeignKeys = new List<ForeignKeySnapshot>
                    {
                        new() { ConstraintName = "fk_orders_user", ColumnName = "user_id", ForeignTableName = "users", ForeignColumnName = "id", OnDelete = "CASCADE" }
                    }
                }
            }
        };

        var json = _service.SerializeSnapshot(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var deserialized = _service.DeserializeSnapshot(json);

        Assert.Equal("ecommerce", deserialized.DatabaseName);
        Assert.Equal("PostgreSQL", deserialized.EngineType);
        Assert.Single(deserialized.Tables);

        var table = deserialized.Tables[0];
        Assert.Equal("orders", table.Name);
        Assert.Equal(2, table.Columns.Count);
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.Single(table.Indexes);
        Assert.Equal("idx_orders_total", table.Indexes[0].Name);
        Assert.Single(table.ForeignKeys);
        Assert.Equal("CASCADE", table.ForeignKeys[0].OnDelete);
    }

    [Fact]
    public void CompareSnapshots_IdentifiesAddedModifiedAndRemovedTables()
    {
        var source = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "users",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "email", DataType = "text" }
                    }
                },
                new()
                {
                    Name = "orders",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "amount", DataType = "numeric" },
                        new() { Name = "status", DataType = "text" } // Added column
                    }
                }
            }
        };

        var target = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "orders",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "amount", DataType = "numeric" }
                    }
                },
                new()
                {
                    Name = "legacy_logs", // Removed from source
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer" }
                    }
                }
            }
        };

        var diff = _service.CompareSnapshots(source, target);

        Assert.True(diff.HasDifferences);
        Assert.Equal(1, diff.AddedCount); // users
        Assert.Equal(1, diff.ModifiedCount); // orders
        Assert.Equal(1, diff.RemovedCount); // legacy_logs

        var usersDiff = Assert.Single(diff.Tables, t => t.Name == "users");
        Assert.Equal(DiffStatus.Added, usersDiff.Status);

        var ordersDiff = Assert.Single(diff.Tables, t => t.Name == "orders");
        Assert.Equal(DiffStatus.Modified, ordersDiff.Status);
        Assert.Contains(ordersDiff.ColumnDiffs, c => c.ColumnName == "status" && c.Status == DiffStatus.Added);

        var logsDiff = Assert.Single(diff.Tables, t => t.Name == "legacy_logs");
        Assert.Equal(DiffStatus.Removed, logsDiff.Status);
    }

    [Fact]
    public void CompareSnapshots_NormalizesEquivalentDataTypes()
    {
        var source = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "items",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "qty", DataType = "int4" },
                        new() { Name = "description", DataType = "varchar(200)" }
                    }
                }
            }
        };

        var target = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "items",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "qty", DataType = "integer" },
                        new() { Name = "description", DataType = "text" }
                    }
                }
            }
        };

        var diff = _service.CompareSnapshots(source, target);

        Assert.Equal(1, diff.IdenticalCount);
        Assert.False(diff.HasDifferences);
        var table = diff.Tables[0];
        Assert.Equal(DiffStatus.Identical, table.Status);
    }

    [Fact]
    public void GenerateSyncDdl_SafeModeOmitsDropStatements()
    {
        var source = new SchemaSnapshotModel();
        var target = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "obsolete_table",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer" }
                    }
                }
            }
        };

        var diff = _service.CompareSnapshots(source, target);
        var options = new SyncOptions
        {
            TargetDialect = DatabaseType.PostgreSQL,
            SafeMode = true
        };

        var ddl = _service.GenerateSyncDdl(diff, options);

        Assert.Contains("-- [SAFE MODE] DROP TABLE \"public\".\"obsolete_table\";", ddl);
        Assert.DoesNotContain("\nDROP TABLE \"public\".\"obsolete_table\";", ddl);
        Assert.Contains("Modo Seguro", ddl);
    }

    [Fact]
    public void GenerateSyncDdl_UnsafeModeEmitsDropStatements()
    {
        var source = new SchemaSnapshotModel();
        var target = new SchemaSnapshotModel
        {
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "obsolete_table",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer" }
                    }
                }
            }
        };

        var diff = _service.CompareSnapshots(source, target);
        var options = new SyncOptions
        {
            TargetDialect = DatabaseType.PostgreSQL,
            SafeMode = false
        };

        var ddl = _service.GenerateSyncDdl(diff, options);

        Assert.Contains("DROP TABLE \"public\".\"obsolete_table\";", ddl);
    }
}
