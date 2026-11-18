using System;
using System.Collections.Generic;
using System.Text.Json;
using Runix.Core.Constants;
using Runix.Core.Models.Diff;
using Xunit;

namespace Runix.Core.Tests.Models;

public class SchemaSnapshotModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void SchemaSnapshotEnvelope_SerializationRoundtrip_PreservesAllProperties()
    {
        var envelope = new SchemaSnapshotEnvelope
        {
            FormatVersion = 1,
            AppVersion = AppConstants.AppVersion,
            ExportedAt = DateTime.UtcNow,
            ChecksumSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Metadata = new SnapshotMetadata
            {
                DatabaseName = "ecommerce_prod",
                EngineType = "PostgreSQL",
                ServerVersion = "PostgreSQL 16.2",
                TableCount = 12,
                ViewCount = 3,
                TriggerCount = 2,
                Label = "v1.4-baseline",
                Notes = "Pre-migration baseline snapshot",
                Environment = "Production"
            },
            Snapshot = new SchemaSnapshotModel
            {
                DatabaseName = "ecommerce_prod",
                EngineType = "PostgreSQL",
                Label = "v1.4-baseline",
                Notes = "Pre-migration baseline snapshot",
                IsIntegrityCompromised = false,
                Tables = new List<TableSnapshot>
                {
                    new()
                    {
                        Name = "users",
                        Schema = "public",
                        Comment = "User account records",
                        Columns = new List<ColumnSnapshot>
                        {
                            new() { Name = "id", DataType = "integer", IsPrimaryKey = true, IsNullable = false },
                            new() { Name = "email", DataType = "varchar(255)", IsNullable = false }
                        },
                        CheckConstraints = new List<CheckConstraintSnapshot>
                        {
                            new() { Name = "chk_users_email_not_empty", Expression = "email <> ''", ColumnName = "email" }
                        }
                    }
                },
                Views = new List<ViewSnapshot>
                {
                    new()
                    {
                        Name = "active_users_view",
                        Schema = "public",
                        DefinitionDdl = "CREATE VIEW active_users_view AS SELECT id, email FROM users WHERE active = true;",
                        Comment = "Active users only"
                    }
                },
                Triggers = new List<TriggerSnapshot>
                {
                    new()
                    {
                        Name = "trg_update_timestamp",
                        Schema = "public",
                        TargetTableName = "users",
                        Timing = "BEFORE",
                        Event = "UPDATE",
                        DefinitionDdl = "CREATE TRIGGER trg_update_timestamp BEFORE UPDATE ON users FOR EACH ROW EXECUTE PROCEDURE set_timestamp();"
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SchemaSnapshotEnvelope>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.FormatVersion);
        Assert.Equal(AppConstants.AppVersion, deserialized.AppVersion);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", deserialized.ChecksumSha256);

        // Metadata assertions
        Assert.Equal("ecommerce_prod", deserialized.Metadata.DatabaseName);
        Assert.Equal("PostgreSQL", deserialized.Metadata.EngineType);
        Assert.Equal(12, deserialized.Metadata.TableCount);
        Assert.Equal(3, deserialized.Metadata.ViewCount);
        Assert.Equal(2, deserialized.Metadata.TriggerCount);
        Assert.Equal("v1.4-baseline", deserialized.Metadata.Label);
        Assert.Equal("Pre-migration baseline snapshot", deserialized.Metadata.Notes);

        // Snapshot model assertions
        Assert.Single(deserialized.Snapshot.Tables);
        var table = deserialized.Snapshot.Tables[0];
        Assert.Equal("users", table.Name);
        Assert.Equal("public.users", table.FullName);
        Assert.Equal("User account records", table.Comment);
        Assert.Equal(2, table.Columns.Count);
        Assert.Single(table.CheckConstraints);
        Assert.Equal("chk_users_email_not_empty", table.CheckConstraints[0].Name);
        Assert.Equal("email <> ''", table.CheckConstraints[0].Expression);
        Assert.Equal("email", table.CheckConstraints[0].ColumnName);

        // View assertions
        Assert.Single(deserialized.Snapshot.Views);
        var view = deserialized.Snapshot.Views[0];
        Assert.Equal("active_users_view", view.Name);
        Assert.Equal("public.active_users_view", view.FullName);
        Assert.Contains("CREATE VIEW active_users_view", view.DefinitionDdl);
        Assert.Equal("Active users only", view.Comment);

        // Trigger assertions
        Assert.Single(deserialized.Snapshot.Triggers);
        var trigger = deserialized.Snapshot.Triggers[0];
        Assert.Equal("trg_update_timestamp", trigger.Name);
        Assert.Equal("users", trigger.TargetTableName);
        Assert.Equal("BEFORE", trigger.Timing);
        Assert.Equal("UPDATE", trigger.Event);
    }

    [Fact]
    public void ViewSnapshot_FullName_FormatsCorrectly()
    {
        var viewWithSchema = new ViewSnapshot { Name = "orders_summary", Schema = "sales" };
        var viewWithoutSchema = new ViewSnapshot { Name = "temp_view", Schema = "" };

        Assert.Equal("sales.orders_summary", viewWithSchema.FullName);
        Assert.Equal("temp_view", viewWithoutSchema.FullName);
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(15728640, "15.00 MB")]
    public void SnapshotCatalogItem_FormattedFileSize_FormatsAccurately(long bytes, string expectedFormatted)
    {
        var item = new SnapshotCatalogItem { FileSizeBytes = bytes };
        Assert.Equal(expectedFormatted, item.FormattedFileSize);
        Assert.Equal(expectedFormatted, SnapshotCatalogItem.FormatFileSize(bytes));
    }

    [Fact]
    public void SnapshotExtractionOptions_DefaultValues_AreAllTrue()
    {
        var options = new SnapshotExtractionOptions();

        Assert.True(options.IncludeTables);
        Assert.True(options.IncludeForeignKeys);
        Assert.True(options.IncludeIndexes);
        Assert.True(options.IncludeViews);
        Assert.True(options.IncludeTriggers);
        Assert.True(options.IncludeCheckConstraints);
    }
}
