using System;
using System.Collections.Generic;
using System.Text.Json;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class WorkspaceSessionModelsTests
{
    [Fact]
    public void ConnectionConfig_GetSessionKey_ProducesConsistentKeys()
    {
        var sqlite1 = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = "/path/to/demo.db"
        };

        var sqlite2 = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = "/path/to/demo.db"
        };

        Assert.Equal(sqlite1.GetSessionKey(), sqlite2.GetSessionKey());
        Assert.StartsWith("sqlite:/path/to/demo.db", sqlite1.GetSessionKey(), StringComparison.OrdinalIgnoreCase);

        var pg1 = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "db.example.com",
            Port = 5432,
            Database = "production",
            Username = "admin"
        };

        var pg2 = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "DB.EXAMPLE.COM",
            Port = 5432,
            Database = "PRODUCTION",
            Username = "ADMIN"
        };

        Assert.Equal(pg1.GetSessionKey(), pg2.GetSessionKey());
        Assert.Equal("postgres:db.example.com:5432:production:admin", pg1.GetSessionKey());
    }

    [Fact]
    public void WorkspaceSessionState_SerializationRoundTrip_PreservesAllTabProperties()
    {
        var session = new WorkspaceSessionState
        {
            ConnectionKey = "sqlite:/data/test.db",
            ActiveTabIndex = 1,
            LastSavedAt = DateTime.UtcNow,
            Tabs = new List<WorkspaceTabSession>
            {
                new()
                {
                    Title = "Query 1",
                    TabType = WorkspaceTabType.Query,
                    SqlText = "SELECT * FROM users WHERE active = 1;",
                    CaretOffset = 25,
                    VerticalScrollOffset = 120.5,
                    HorizontalScrollOffset = 10.0,
                    Tag = "query"
                },
                new()
                {
                    Title = "users",
                    TabType = WorkspaceTabType.TableData,
                    TableName = "users",
                    FilterText = "age > 18",
                    PageNumber = 2,
                    Tag = "data:users"
                },
                new()
                {
                    Title = "orders (Structure)",
                    TabType = WorkspaceTabType.TableStructure,
                    TableName = "orders",
                    Tag = "struct:orders"
                },
                new()
                {
                    Title = "Diagrama ERD",
                    TabType = WorkspaceTabType.Erd,
                    Tag = "erd"
                }
            }
        };

        var json = JsonSerializer.Serialize(session);
        var restored = JsonSerializer.Deserialize<WorkspaceSessionState>(json);

        Assert.NotNull(restored);
        Assert.Equal("sqlite:/data/test.db", restored!.ConnectionKey);
        Assert.Equal(1, restored.ActiveTabIndex);
        Assert.Equal(4, restored.Tabs.Count);

        // Verify Query Tab
        var qTab = restored.Tabs[0];
        Assert.Equal("Query 1", qTab.Title);
        Assert.Equal(WorkspaceTabType.Query, qTab.TabType);
        Assert.Equal("SELECT * FROM users WHERE active = 1;", qTab.SqlText);
        Assert.Equal(25, qTab.CaretOffset);
        Assert.Equal(120.5, qTab.VerticalScrollOffset);
        Assert.Equal(10.0, qTab.HorizontalScrollOffset);

        // Verify TableData Tab
        var tTab = restored.Tabs[1];
        Assert.Equal("users", tTab.Title);
        Assert.Equal(WorkspaceTabType.TableData, tTab.TabType);
        Assert.Equal("users", tTab.TableName);
        Assert.Equal("age > 18", tTab.FilterText);
        Assert.Equal(2, tTab.PageNumber);

        // Verify Structure Tab
        var sTab = restored.Tabs[2];
        Assert.Equal("orders", sTab.TableName);
        Assert.Equal(WorkspaceTabType.TableStructure, sTab.TabType);

        // Verify ERD Tab
        var eTab = restored.Tabs[3];
        Assert.Equal(WorkspaceTabType.Erd, eTab.TabType);
    }
}
