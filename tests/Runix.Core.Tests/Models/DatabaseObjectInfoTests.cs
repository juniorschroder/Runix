using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class DatabaseObjectInfoTests
{
    [Fact]
    public void DatabaseObjectInfo_PropertiesAndBadges_ResolveCorrectly()
    {
        var trigger = new DatabaseObjectInfo
        {
            Schema = "public",
            Name = "trg_audit_users",
            ObjectType = DatabaseObjectType.Trigger,
            TargetTableName = "users"
        };

        Assert.Equal("trg_audit_users", trigger.QualifiedName);
        Assert.Equal("TRG", trigger.TypeBadge);
        Assert.Equal("⚡", trigger.Icon);
        Assert.Equal("on users", trigger.Subtitle);

        var fn = new DatabaseObjectInfo
        {
            Schema = "analytics",
            Name = "calc_revenue",
            ObjectType = DatabaseObjectType.Function,
            ReturnType = "numeric"
        };

        Assert.Equal("analytics.calc_revenue", fn.QualifiedName);
        Assert.Equal("FUNC", fn.TypeBadge);
        Assert.Equal("ƒ", fn.Icon);
        Assert.Equal("→ numeric", fn.Subtitle);
    }

    [Fact]
    public void DatabaseObjectTemplateGenerator_GeneratesDialectSpecificTemplates()
    {
        var pgView = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.View, DatabaseType.PostgreSQL, "v_active_users", "users");
        Assert.Contains("CREATE OR REPLACE VIEW \"v_active_users\"", pgView);

        var sqliteView = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.View, DatabaseType.SQLite, "v_active_users", "users");
        Assert.Contains("CREATE VIEW \"v_active_users\"", sqliteView);
        Assert.DoesNotContain("OR REPLACE", sqliteView);

        var sqliteTrigger = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.Trigger, DatabaseType.SQLite, "trg_log", "orders");
        Assert.Contains("CREATE TRIGGER \"trg_log\"", sqliteTrigger);
        Assert.Contains("AFTER INSERT ON \"orders\"", sqliteTrigger);
    }

    [Fact]
    public void ProgrammabilitySidebarItem_MapsMetadataCorrectly()
    {
        var idx = new DatabaseObjectInfo
        {
            Name = "idx_orders_customer",
            ObjectType = DatabaseObjectType.Index,
            TargetTableName = "orders",
            IsUnique = true
        };

        var sidebarItem = new ProgrammabilitySidebarItem(idx);
        Assert.Equal(SidebarItemType.Index, sidebarItem.ItemType);
        Assert.Equal("IDX", sidebarItem.TypeBadge);
        Assert.True(sidebarItem.HasSubtitle);
        Assert.Equal("UNIQUE on orders", sidebarItem.Subtitle);
    }
}
