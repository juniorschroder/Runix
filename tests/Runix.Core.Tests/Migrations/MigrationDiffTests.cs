using System.Collections.Generic;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Migrations;

public class MigrationDiffTests
{
    [Fact]
    public void Calculate_ShouldDetectNewTable_WhenOriginalIsNull()
    {
        // Arrange
        var modified = new ErdTableNode
        {
            Table = new TableInfo { Name = "orders" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "total", DataType = "REAL" }
            }
        };

        // Act
        var diff = TableSchemaDiff.Calculate(null, modified);

        // Assert
        Assert.True(diff.IsNewTable);
        Assert.Equal("orders", diff.TargetTableName);
        Assert.Equal(2, diff.AddedColumns.Count);
        Assert.Empty(diff.RemovedColumns);
        Assert.Empty(diff.AlteredColumns);
    }

    [Fact]
    public void Calculate_ShouldDetectRenamedTable()
    {
        // Arrange
        var original = new ErdTableNode
        {
            Table = new TableInfo { Name = "old_orders" },
            Columns = new List<ColumnInfo> { new() { Name = "id", DataType = "INTEGER" } }
        };
        var modified = new ErdTableNode
        {
            Table = new TableInfo { Name = "new_orders" },
            Columns = new List<ColumnInfo> { new() { Name = "id", DataType = "INTEGER" } }
        };

        // Act
        var diff = TableSchemaDiff.Calculate(original, modified);

        // Assert
        Assert.False(diff.IsNewTable);
        Assert.True(diff.IsRenamed);
        Assert.Equal("old_orders", diff.OriginalTableName);
        Assert.Equal("new_orders", diff.TargetTableName);
    }

    [Fact]
    public void Calculate_ShouldDetectAddedAndRemovedColumns()
    {
        // Arrange
        var original = new ErdTableNode
        {
            Table = new TableInfo { Name = "users" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "username", DataType = "TEXT" },
                new() { Name = "deprecated_token", DataType = "TEXT" }
            }
        };
        var modified = new ErdTableNode
        {
            Table = new TableInfo { Name = "users" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "username", DataType = "TEXT" },
                new() { Name = "email", DataType = "VARCHAR(255)", IsNullable = false }
            }
        };

        // Act
        var diff = TableSchemaDiff.Calculate(original, modified);

        // Assert
        Assert.Single(diff.AddedColumns);
        Assert.Equal("email", diff.AddedColumns[0].Name);

        Assert.Single(diff.RemovedColumns);
        Assert.Equal("deprecated_token", diff.RemovedColumns[0].Name);

        Assert.Empty(diff.AlteredColumns);
    }

    [Fact]
    public void Calculate_ShouldDetectAlteredColumns()
    {
        // Arrange
        var original = new ErdTableNode
        {
            Table = new TableInfo { Name = "products" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "price", DataType = "REAL", IsNullable = true }
            }
        };
        var modified = new ErdTableNode
        {
            Table = new TableInfo { Name = "products" },
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "BIGINT", IsPrimaryKey = true },
                new() { Name = "price", DataType = "NUMERIC(10,2)", IsNullable = false }
            }
        };

        // Act
        var diff = TableSchemaDiff.Calculate(original, modified);

        // Assert
        Assert.Empty(diff.AddedColumns);
        Assert.Empty(diff.RemovedColumns);
        Assert.Equal(2, diff.AlteredColumns.Count);

        var idAlter = diff.AlteredColumns.Find(a => a.NewCol.Name == "id");
        Assert.Equal("INTEGER", idAlter.OldCol.DataType);
        Assert.Equal("BIGINT", idAlter.NewCol.DataType);

        var priceAlter = diff.AlteredColumns.Find(a => a.NewCol.Name == "price");
        Assert.True(priceAlter.OldCol.IsNullable);
        Assert.False(priceAlter.NewCol.IsNullable);
    }

    [Fact]
    public void Calculate_ShouldDetectAddedForeignKeys()
    {
        // Arrange
        var original = new ErdTableNode
        {
            Table = new TableInfo { Name = "orders" },
            Columns = new List<ColumnInfo> { new() { Name = "user_id", DataType = "INTEGER" } }
        };
        var modified = new ErdTableNode
        {
            Table = new TableInfo { Name = "orders" },
            Columns = new List<ColumnInfo> { new() { Name = "user_id", DataType = "INTEGER" } }
        };
        var newFk = new ForeignKeyDefinition
        {
            SourceTable = "orders",
            SourceColumn = "user_id",
            TargetTable = "users",
            TargetColumn = "id",
            OnDelete = "CASCADE"
        };

        // Act
        var diff = TableSchemaDiff.Calculate(original, modified, addedFks: new[] { newFk });

        // Assert
        Assert.Single(diff.AddedForeignKeys);
        Assert.Equal("CASCADE", diff.AddedForeignKeys[0].OnDelete);
        Assert.True(diff.HasChanges);
    }
}
