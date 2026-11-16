using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Storage;

public class AppStorageServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly AppStorageService _storage;

    public AppStorageServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"runix_app_test_{Guid.NewGuid():N}.db");
        _storage = new AppStorageService(_testDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    [Fact]
    public async Task SavedQueries_CRUD_WorksAsExpected()
    {
        // 1. Initialize
        await _storage.InitializeAsync();

        // 2. Save query
        var query = new SavedQuery
        {
            Title = "Active Customers Monthly",
            Description = "Lists customers with orders in the last 30 days",
            SqlText = "SELECT * FROM customers WHERE active = 1;",
            Tags = new List<string> { "monthly", "customers", "report" },
            IsFavorite = true
        };

        await _storage.SaveQueryAsync(query);

        // 3. Retrieve by ID
        var fetched = await _storage.GetSavedQueryByIdAsync(query.Id);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Active Customers Monthly");
        fetched.Tags.Should().Contain("monthly");
        fetched.IsFavorite.Should().BeTrue();

        // 4. Search Filter
        var filteredByTag = await _storage.GetSavedQueriesAsync(searchFilter: "report");
        filteredByTag.Should().HaveCount(1);

        var filteredNotFound = await _storage.GetSavedQueriesAsync(searchFilter: "non_existent");
        filteredNotFound.Should().BeEmpty();

        // 5. Toggle favorite
        await _storage.ToggleFavoriteAsync(query.Id, false);
        var updated = await _storage.GetSavedQueryByIdAsync(query.Id);
        updated!.IsFavorite.Should().BeFalse();

        // 6. Delete
        await _storage.DeleteSavedQueryAsync(query.Id);
        var afterDelete = await _storage.GetSavedQueryByIdAsync(query.Id);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task CustomSnippets_CRUD_WorksAsExpected()
    {
        await _storage.InitializeAsync();

        var snippet = new CustomSnippet
        {
            Prefix = "selc",
            Title = "Select Cheeses",
            Description = "Quick cheeses query",
            Body = "SELECT id, name, price FROM cheeses WHERE in_stock = 1;"
        };

        await _storage.SaveCustomSnippetAsync(snippet);

        var snippets = await _storage.GetCustomSnippetsAsync();
        snippets.Should().ContainSingle(s => s.Prefix == "selc");

        // Delete
        await _storage.DeleteCustomSnippetAsync(snippet.Id);
        var afterDelete = await _storage.GetCustomSnippetsAsync();
        afterDelete.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryHistory_LogAndQuery_WorksAsExpected()
    {
        await _storage.InitializeAsync();

        var entry1 = new QueryHistoryEntry
        {
            SqlText = "SELECT COUNT(*) FROM cheeses;",
            ElapsedMilliseconds = 14,
            RowCount = 1,
            Success = true,
            ExecutedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var entry2 = new QueryHistoryEntry
        {
            SqlText = "SELECT * FROM invalid_table;",
            ElapsedMilliseconds = 3,
            Success = false,
            ErrorMessage = "no such table: invalid_table",
            ExecutedAt = DateTime.UtcNow
        };

        await _storage.LogQueryExecutionAsync(entry1);
        await _storage.LogQueryExecutionAsync(entry2);

        var history = await _storage.GetQueryHistoryAsync(limit: 10);
        history.Should().HaveCount(2);

        // Most recent first
        history[0].SqlText.Should().Be("SELECT * FROM invalid_table;");
        history[0].Success.Should().BeFalse();

        history[1].SqlText.Should().Be("SELECT COUNT(*) FROM cheeses;");
        history[1].Success.Should().BeTrue();
        history[1].ElapsedMilliseconds.Should().Be(14);

        // Clear
        await _storage.ClearQueryHistoryAsync();
        var afterClear = await _storage.GetQueryHistoryAsync();
        afterClear.Should().BeEmpty();
    }

    [Fact]
    public async Task SavedQueries_FavoritesOnly_And_ConnectionScope_FiltersWork()
    {
        await _storage.InitializeAsync();

        var q1 = new SavedQuery
        {
            Title = "Global Fav",
            SqlText = "SELECT 1;",
            IsFavorite = true,
            ConnectionId = null
        };
        var q2 = new SavedQuery
        {
            Title = "Conn1 NonFav",
            SqlText = "SELECT 2;",
            IsFavorite = false,
            ConnectionId = "conn_1"
        };
        var q3 = new SavedQuery
        {
            Title = "Conn1 Fav",
            SqlText = "SELECT 3;",
            IsFavorite = true,
            ConnectionId = "conn_1"
        };

        await _storage.SaveQueryAsync(q1);
        await _storage.SaveQueryAsync(q2);
        await _storage.SaveQueryAsync(q3);

        // Only favorites
        var favs = await _storage.GetSavedQueriesAsync(onlyFavorites: true);
        favs.Should().HaveCount(2);
        favs.Should().Contain(q => q.Title == "Global Fav");
        favs.Should().Contain(q => q.Title == "Conn1 Fav");

        // Connection scoped (should include conn_1 and global)
        var conn1Queries = await _storage.GetSavedQueriesAsync(connectionId: "conn_1");
        conn1Queries.Should().HaveCount(3);

        // Another connection (should only see global)
        var conn2Queries = await _storage.GetSavedQueriesAsync(connectionId: "conn_2");
        conn2Queries.Should().HaveCount(1);
        conn2Queries[0].Title.Should().Be("Global Fav");
    }

    [Fact]
    public async Task WorkspaceSession_Save_Get_Update_Clear_WorksAsExpected()
    {
        await _storage.InitializeAsync();

        var connectionKey = "sqlite:/var/data/app.db";

        // 1. Initial get should return null
        var empty = await _storage.GetWorkspaceSessionAsync(connectionKey);
        empty.Should().BeNull();

        // 2. Save session with SQL drafts, carets and scroll offsets
        var session = new WorkspaceSessionState
        {
            ConnectionKey = connectionKey,
            ActiveTabIndex = 1,
            Tabs = new List<WorkspaceTabSession>
            {
                new()
                {
                    Title = "Query 1",
                    TabType = WorkspaceTabType.Query,
                    SqlText = "SELECT id, name FROM customers;\n-- Unsaved draft\nSELECT * FROM orders;",
                    CaretOffset = 45,
                    VerticalScrollOffset = 80.0,
                    HorizontalScrollOffset = 0.0
                },
                new()
                {
                    Title = "customers",
                    TabType = WorkspaceTabType.TableData,
                    TableName = "customers",
                    FilterText = "active = 1"
                }
            }
        };

        await _storage.SaveWorkspaceSessionAsync(session);

        // 3. Fetch session
        var fetched = await _storage.GetWorkspaceSessionAsync(connectionKey);
        fetched.Should().NotBeNull();
        fetched!.ConnectionKey.Should().Be(connectionKey);
        fetched.ActiveTabIndex.Should().Be(1);
        fetched.Tabs.Should().HaveCount(2);
        fetched.Tabs[0].SqlText.Should().Contain("Unsaved draft");
        fetched.Tabs[0].CaretOffset.Should().Be(45);
        fetched.Tabs[0].VerticalScrollOffset.Should().Be(80.0);
        fetched.Tabs[1].TableName.Should().Be("customers");
        fetched.Tabs[1].FilterText.Should().Be("active = 1");

        // 4. Update session (upsert on conflict)
        session.ActiveTabIndex = 0;
        session.Tabs[0].SqlText = "SELECT count(*) FROM customers;";
        await _storage.SaveWorkspaceSessionAsync(session);

        var updated = await _storage.GetWorkspaceSessionAsync(connectionKey);
        updated.Should().NotBeNull();
        updated!.ActiveTabIndex.Should().Be(0);
        updated.Tabs[0].SqlText.Should().Be("SELECT count(*) FROM customers;");

        // 5. Clear session
        await _storage.ClearWorkspaceSessionAsync(connectionKey);
        var cleared = await _storage.GetWorkspaceSessionAsync(connectionKey);
        cleared.Should().BeNull();
    }
}
