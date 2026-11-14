using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class CustomSnippetCompletionTests
{
    private readonly SqlCompletionService _completionService = new();

    [Fact]
    public void GetCompletions_WithCustomSnippets_ReturnsSnippetCompletionItem()
    {
        // Arrange
        var context = new SqlContextInfo
        {
            CurrentWord = "myj",
            ActiveClause = "SELECT"
        };
        var tables = new List<TableInfo>();
        var customSnippets = new List<CustomSnippet>
        {
            new()
            {
                Prefix = "myjoin",
                Title = "Custom Inner Join",
                Description = "Joins orders and customers",
                Body = "INNER JOIN customers c ON c.id = o.customer_id"
            },
            new()
            {
                Prefix = "seltop",
                Title = "Select Top 10",
                Body = "SELECT * FROM {0} LIMIT 10;"
            }
        };

        // Act
        var completions = _completionService.GetCompletions(context, tables, null, customSnippets);

        // Assert
        completions.Should().NotBeEmpty();
        var myjoinMatch = completions.FirstOrDefault(c => c.Label == "myjoin");
        myjoinMatch.Should().NotBeNull();
        myjoinMatch!.Kind.Should().Be(CompletionItemKind.Snippet);
        myjoinMatch.Detail.Should().Contain("Custom Inner Join");
        myjoinMatch.InsertText.Should().Be("INNER JOIN customers c ON c.id = o.customer_id");
        myjoinMatch.BadgeText.Should().Be("[SNP]");
    }

    [Fact]
    public void GetCompletions_MatchesSnippetByTitle_WhenPrefixDoesNotDirectlyMatch()
    {
        // Arrange
        var context = new SqlContextInfo
        {
            CurrentWord = "relat",
            ActiveClause = "SELECT"
        };
        var tables = new List<TableInfo>();
        var customSnippets = new List<CustomSnippet>
        {
            new()
            {
                Prefix = "rep_vendas",
                Title = "Relatório de Vendas Mensais",
                Body = "SELECT strftime('%Y-%m', data), sum(valor) FROM vendas GROUP BY 1;"
            }
        };

        // Act
        var completions = _completionService.GetCompletions(context, tables, null, customSnippets);

        // Assert
        var match = completions.FirstOrDefault(c => c.Label == "rep_vendas");
        match.Should().NotBeNull();
        match!.Kind.Should().Be(CompletionItemKind.Snippet);
    }
}
