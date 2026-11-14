namespace Runix.Core.Tests;

using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

public class DmlGeneratorServiceTests
{
    [Fact]
    public void StagedChangeset_Tracking_CalculatesTotalChangesAccurately()
    {
        var changeset = new StagedChangeset
        {
            TableName = "cheeses",
            PrimaryKeyColumns = new[] { "id" }
        };

        Assert.False(changeset.HasChanges);
        Assert.Equal(0, changeset.TotalChangesCount);

        // Track cell edit
        changeset.TrackCellEdit(0, "name", "Brie", "Camembert");
        Assert.True(changeset.HasChanges);
        Assert.Equal(1, changeset.ModifiedRowsCount);
        Assert.Equal(1, changeset.TotalChangesCount);

        // Edit another cell on the SAME row
        changeset.TrackCellEdit(0, "price", 10.0, 12.5);
        Assert.Equal(1, changeset.ModifiedRowsCount); // still 1 modified row

        // Revert first cell back to original value
        changeset.TrackCellEdit(0, "name", "Brie", "Brie");
        Assert.Equal(1, changeset.ModifiedRowsCount); // price is still modified

        // Revert price back to original
        changeset.TrackCellEdit(0, "price", 10.0, 10.0);
        Assert.Equal(0, changeset.ModifiedRowsCount);
        Assert.False(changeset.HasChanges);

        // Add row
        changeset.AddRow(new Dictionary<string, object?> { ["name"] = "Gouda", ["price"] = 8.0 });
        Assert.Equal(1, changeset.AddedRowsCount);
        Assert.Equal(1, changeset.TotalChangesCount);

        // Mark row deleted
        changeset.MarkRowDeleted(new Dictionary<string, object?> { ["id"] = 99, ["name"] = "Old" });
        Assert.Equal(1, changeset.DeletedRowsCount);
        Assert.Equal(2, changeset.TotalChangesCount);

        // Clear
        changeset.Clear();
        Assert.Equal(0, changeset.TotalChangesCount);
        Assert.False(changeset.HasChanges);
    }

    [Fact]
    public void SqliteDmlGenerator_GenerateUpdate_WithSinglePrimaryKey_ProducesValidParameterizedSql()
    {
        var generator = new SqliteDmlGeneratorService();
        var pks = new[] { "id" };
        var targetPk = new Dictionary<string, object?> { ["id"] = 42 };
        var modified = new Dictionary<string, object?> { ["name"] = "Gruyère", ["stock"] = 15 };

        var cmd = generator.GenerateUpdate("cheeses", pks, targetPk, modified);

        Assert.Equal("UPDATE \"cheeses\" SET \"name\" = @p0, \"stock\" = @p1 WHERE \"id\" = @p2;", cmd.Sql);
        Assert.Equal("Gruyère", cmd.Parameters["@p0"]);
        Assert.Equal(15, cmd.Parameters["@p1"]);
        Assert.Equal(42, cmd.Parameters["@p2"]);
    }

    [Fact]
    public void SqliteDmlGenerator_GenerateUpdate_WithCompositePrimaryKey_ProducesMultipleWhereClauses()
    {
        var generator = new SqliteDmlGeneratorService();
        var pks = new[] { "order_id", "item_id" };
        var targetPk = new Dictionary<string, object?> { ["order_id"] = 101, ["item_id"] = 5 };
        var modified = new Dictionary<string, object?> { ["quantity"] = 3 };

        var cmd = generator.GenerateUpdate("order_items", pks, targetPk, modified);

        Assert.Equal("UPDATE \"order_items\" SET \"quantity\" = @p0 WHERE \"order_id\" = @p1 AND \"item_id\" = @p2;", cmd.Sql);
        Assert.Equal(3, cmd.Parameters["@p0"]);
        Assert.Equal(101, cmd.Parameters["@p1"]);
        Assert.Equal(5, cmd.Parameters["@p2"]);
    }

    [Fact]
    public void SqliteDmlGenerator_GenerateUpdate_WithoutPrimaryKey_FallsBackToRowId()
    {
        var generator = new SqliteDmlGeneratorService();
        var pks = Array.Empty<string>();
        var targetPk = new Dictionary<string, object?> { ["_rowid_"] = 77L };
        var modified = new Dictionary<string, object?> { ["notes"] = "Updated note" };

        var cmd = generator.GenerateUpdate("log_table", pks, targetPk, modified);

        Assert.Equal("UPDATE \"log_table\" SET \"notes\" = @p0 WHERE _rowid_ = @p1;", cmd.Sql);
        Assert.Equal("Updated note", cmd.Parameters["@p0"]);
        Assert.Equal(77L, cmd.Parameters["@p1"]);
    }

    [Fact]
    public void SqliteDmlGenerator_GenerateInsert_BindsDbNullForNullValues()
    {
        var generator = new SqliteDmlGeneratorService();
        var values = new Dictionary<string, object?>
        {
            ["name"] = "Cheddar",
            ["description"] = null,
            ["rating"] = DBNull.Value
        };

        var cmd = generator.GenerateInsert("cheeses", values);

        Assert.Equal("INSERT INTO \"cheeses\" (\"name\", \"description\", \"rating\") VALUES (@p0, @p1, @p2);", cmd.Sql);
        Assert.Equal("Cheddar", cmd.Parameters["@p0"]);
        Assert.Equal(DBNull.Value, cmd.Parameters["@p1"]);
        Assert.Equal(DBNull.Value, cmd.Parameters["@p2"]);
    }

    [Fact]
    public void SqliteDmlGenerator_GenerateDelete_TargetsPrimaryKey()
    {
        var generator = new SqliteDmlGeneratorService();
        var pks = new[] { "id" };
        var targetPk = new Dictionary<string, object?> { ["id"] = 12 };

        var cmd = generator.GenerateDelete("cheeses", pks, targetPk);

        Assert.Equal("DELETE FROM \"cheeses\" WHERE \"id\" = @p0;", cmd.Sql);
        Assert.Equal(12, cmd.Parameters["@p0"]);
    }

    [Fact]
    public void PostgresDmlGenerator_GenerateBatch_ProducesConsistentCommands()
    {
        var generator = new PostgresDmlGeneratorService();
        var changeset = new StagedChangeset
        {
            TableName = "products",
            PrimaryKeyColumns = new[] { "id" }
        };

        var originalRows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "Prod A" }
        };

        changeset.TrackCellEdit(0, "name", "Prod A", "Prod A Modified");
        changeset.AddRow(new Dictionary<string, object?> { ["name"] = "Prod New", ["price"] = 99.9 });
        changeset.MarkRowDeleted(new Dictionary<string, object?> { ["id"] = 999 });

        var batch = generator.GenerateBatch(changeset, originalRows);

        Assert.Equal("products", batch.TableName);
        Assert.Equal(3, batch.Commands.Count);
        Assert.StartsWith("UPDATE \"products\"", batch.Commands[0].Sql);
        Assert.StartsWith("INSERT INTO \"products\"", batch.Commands[1].Sql);
        Assert.StartsWith("DELETE FROM \"products\"", batch.Commands[2].Sql);
    }
}
