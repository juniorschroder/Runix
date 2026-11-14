using System.Collections.Generic;
using System.Threading.Tasks;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class SidebarTreeModelsTests
{
    [Fact]
    public void SchemaOrGroupSidebarItem_Initializes_Correctly()
    {
        var group = new SchemaOrGroupSidebarItem("Tabelas", "📁");
        Assert.Equal("Tabelas", group.DisplayName);
        Assert.Equal("📁", group.Icon);
        Assert.Equal("Tabelas (0)", group.HeaderText);
        Assert.True(group.IsExpanded);

        var table = new TableInfo { Name = "users" };
        group.Children.Add(new TableSidebarItem(table));
        group.NotifyCountChanged();

        Assert.Equal(1, group.Count);
        Assert.Equal("Tabelas (1)", group.HeaderText);
    }

    [Fact]
    public void TableSidebarItem_FavoriteToggle_UpdatesIcon()
    {
        var table = new TableInfo { Name = "orders", RowCount = 42 };
        var item = new TableSidebarItem(table, isFavorite: false);

        Assert.Equal("☆", item.FavoriteIcon);
        Assert.False(item.IsFavorite);
        Assert.True(item.HasRowCount);
        Assert.Equal(42, item.RowCount);

        item.IsFavorite = true;
        Assert.Equal("⭐", item.FavoriteIcon);
        Assert.True(item.IsFavorite);
    }

    [Fact]
    public async Task TableSidebarItem_OnExpanded_TriggersLoadColumnsHandler()
    {
        var table = new TableInfo { Name = "products" };
        var item = new TableSidebarItem(table);
        bool handlerCalled = false;

        item.LoadColumnsHandler = t =>
        {
            handlerCalled = true;
            t.Children.Add(new ColumnSidebarItem(new ColumnInfo { Name = "id", DataType = "INTEGER", IsPrimaryKey = true }));
            t.ColumnsLoaded = true;
            return Task.CompletedTask;
        };

        Assert.False(item.ColumnsLoaded);
        Assert.Empty(item.Children);

        item.IsExpanded = true;

        // Allow task to finish
        await Task.Delay(50);

        Assert.True(handlerCalled);
        Assert.True(item.ColumnsLoaded);
        Assert.Single(item.Children);

        var col = Assert.IsType<ColumnSidebarItem>(item.Children[0]);
        Assert.Equal("id", col.DisplayName);
        Assert.Equal("INTEGER", col.DataType);
        Assert.Equal("🔑", col.KeyBadge);
        Assert.True(col.IsPrimaryKey);
    }

    [Fact]
    public void ColumnSidebarItem_KeyBadges_MapCorrectly()
    {
        var pk = new ColumnSidebarItem(new ColumnInfo { Name = "id", DataType = "INT", IsPrimaryKey = true });
        Assert.Equal("🔑", pk.KeyBadge);

        var fk = new ColumnSidebarItem(new ColumnInfo { Name = "user_id", DataType = "INT", IsForeignKey = true });
        Assert.Equal("🔗", fk.KeyBadge);

        var regular = new ColumnSidebarItem(new ColumnInfo { Name = "name", DataType = "TEXT" });
        Assert.Equal("•", regular.KeyBadge);
    }
}
