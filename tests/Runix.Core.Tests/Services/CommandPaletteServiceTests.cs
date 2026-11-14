using System.Collections.Generic;
using System.Linq;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class CommandPaletteServiceTests
{
    private static CommandPaletteService CreateSampleService()
    {
        var service = new CommandPaletteService();
        var actions = new List<CommandPaletteItem>
        {
            new()
            {
                Id = "action:new-query",
                Title = "Nova Consulta SQL",
                Subtitle = "Abrir nova aba de editor SQL",
                Category = CommandPaletteCategory.Action,
                ShortcutHint = "Ctrl+N"
            },
            new()
            {
                Id = "action:health-dashboard",
                Title = "Dashboard de Saúde do Banco",
                Subtitle = "Inspecionar telemetria, cache hit ratio e manutenção VACUUM",
                Category = CommandPaletteCategory.Action
            }
        };

        var tables = new List<TableInfo>
        {
            new() { Name = "users", Schema = "public", RowCount = 120 },
            new() { Name = "user_roles", Schema = "public", RowCount = 45 },
            new() { Name = "active_users_view", Schema = "public", Type = "VIEW" }
        };

        var progObjects = new List<DatabaseObjectInfo>
        {
            new() { Name = "trg_audit_users", Schema = "public", ObjectType = DatabaseObjectType.Trigger, TargetTableName = "users" },
            new() { Name = "idx_users_email", Schema = "public", ObjectType = DatabaseObjectType.Index, TargetTableName = "users" }
        };

        var savedQueries = new List<SavedQuery>
        {
            new() { Id = "q1", Title = "Relatório Mensal de Vendas", SqlText = "SELECT * FROM sales", IsFavorite = true }
        };

        var snippets = new List<CustomSnippet>
        {
            new() { Prefix = "sel100", Title = "Select top 100 rows", Body = "SELECT * FROM {table} LIMIT 100;" }
        };

        service.RebuildCatalog(
            actions,
            tables,
            progObjects,
            savedQueries,
            snippets,
            new HashSet<string> { "users" });

        return service;
    }

    [Fact]
    public void Search_WithPrefixScopes_FiltersByCategory()
    {
        var service = CreateSampleService();

        var actionsOnly = service.Search(">");
        Assert.NotEmpty(actionsOnly);
        Assert.All(actionsOnly, item => Assert.Equal(CommandPaletteCategory.Action, item.Category));

        var tablesAndViews = service.Search("#");
        Assert.Equal(3, tablesAndViews.Count);
        Assert.All(tablesAndViews, item => Assert.True(item.Category is CommandPaletteCategory.Table or CommandPaletteCategory.View));

        var ddlObjects = service.Search("@");
        Assert.Contains(ddlObjects, item => item.Title == "trg_audit_users");
        Assert.Contains(ddlObjects, item => item.Title == "idx_users_email");

        var queriesAndSnippets = service.Search("?");
        Assert.Equal(2, queriesAndSnippets.Count);
        Assert.All(queriesAndSnippets, item => Assert.True(item.Category is CommandPaletteCategory.SavedQuery or CommandPaletteCategory.Snippet));
    }

    [Fact]
    public void Search_MatchesSnakeCaseInitialsAndAcronyms()
    {
        var service = CreateSampleService();

        var results = service.Search("u_r");
        Assert.NotEmpty(results);
        Assert.Equal("public.user_roles", results[0].Title);

        var viewResults = service.Search("auv");
        Assert.Contains(viewResults, x => x.Title.EndsWith("active_users_view"));
    }

    [Fact]
    public void RecordUsage_PromotesRecentlyUsedItemToTop()
    {
        var service = CreateSampleService();
        service.RecordUsage("ddl:Index:public.idx_users_email");

        var emptySearch = service.Search("");
        Assert.NotEmpty(emptySearch);
        Assert.Equal("ddl:Index:public.idx_users_email", emptySearch[0].Id);
    }

    [Fact]
    public void Search_SubsequenceAndSubtitleMatching_FindsRelevantItems()
    {
        var service = CreateSampleService();

        var vacuumSearch = service.Search("VACUUM");
        Assert.Single(vacuumSearch);
        Assert.Equal("action:health-dashboard", vacuumSearch[0].Id);
    }
}
