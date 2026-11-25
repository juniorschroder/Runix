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

        var pkg = new DatabaseObjectInfo
        {
            Schema = "HR",
            Name = "emp_mgmt",
            ObjectType = DatabaseObjectType.Package
        };

        Assert.Equal("HR.emp_mgmt", pkg.QualifiedName);
        Assert.Equal("PKG", pkg.TypeBadge);
        Assert.Equal("📦", pkg.Icon);
        Assert.Equal("PL/SQL Package", pkg.Subtitle);

        var seq = new DatabaseObjectInfo
        {
            Schema = "HR",
            Name = "emp_seq",
            ObjectType = DatabaseObjectType.Sequence
        };

        Assert.Equal("HR.emp_seq", seq.QualifiedName);
        Assert.Equal("SEQ", seq.TypeBadge);
        Assert.Equal("🔢", seq.Icon);
        Assert.Equal("Sequence Generator", seq.Subtitle);
    }

    [Fact]
    public void DatabaseObjectTemplateGenerator_GeneratesDialectSpecificTemplates()
    {
        var pgView = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.View, DatabaseType.PostgreSQL, "v_active_users", "users");
        Assert.Contains("CREATE OR REPLACE VIEW \"v_active_users\"", pgView);

        var oracleView = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.View, DatabaseType.Oracle, "v_emp", "employees");
        Assert.Contains("CREATE OR REPLACE VIEW \"v_emp\"", oracleView);
        Assert.Contains("ROWNUM <= 100", oracleView);

        var sqliteView = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.View, DatabaseType.SQLite, "v_active_users", "users");
        Assert.Contains("CREATE VIEW \"v_active_users\"", sqliteView);
        Assert.DoesNotContain("OR REPLACE", sqliteView);

        var oracleTrigger = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.Trigger, DatabaseType.Oracle, "trg_emp_audit", "employees");
        Assert.Contains("CREATE OR REPLACE TRIGGER \"trg_emp_audit\"", oracleTrigger);
        Assert.Contains("BEFORE INSERT OR UPDATE ON \"employees\"", oracleTrigger);

        var oraclePackage = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.Package, DatabaseType.Oracle, "pkg_payroll", null);
        Assert.Contains("CREATE OR REPLACE PACKAGE \"pkg_payroll\"", oraclePackage);
        Assert.Contains("CREATE OR REPLACE PACKAGE BODY \"pkg_payroll\"", oraclePackage);

        var oracleSequence = DatabaseObjectTemplateGenerator.GenerateTemplate(DatabaseObjectType.Sequence, DatabaseType.Oracle, "seq_orders", null);
        Assert.Contains("CREATE SEQUENCE \"seq_orders\"", oracleSequence);
        Assert.Contains("START WITH 1", oracleSequence);

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

        var pkg = new DatabaseObjectInfo
        {
            Name = "pkg_hr",
            ObjectType = DatabaseObjectType.Package
        };
        var pkgSidebar = new ProgrammabilitySidebarItem(pkg);
        Assert.Equal(SidebarItemType.Package, pkgSidebar.ItemType);
        Assert.Equal("PKG", pkgSidebar.TypeBadge);
        Assert.Equal("📦", pkgSidebar.Icon);

        var seq = new DatabaseObjectInfo
        {
            Name = "seq_users",
            ObjectType = DatabaseObjectType.Sequence
        };
        var seqSidebar = new ProgrammabilitySidebarItem(seq);
        Assert.Equal(SidebarItemType.Sequence, seqSidebar.ItemType);
        Assert.Equal("SEQ", seqSidebar.TypeBadge);
        Assert.Equal("🔢", seqSidebar.Icon);
    }
}
