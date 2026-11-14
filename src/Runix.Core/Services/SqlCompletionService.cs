namespace Runix.Core.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Models;

public class SqlCompletionService : ISqlCompletionService
{
    public IReadOnlyList<CompletionItem> GetCompletions(
        SqlContextInfo context,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>>? tableColumns = null,
        IReadOnlyList<CustomSnippet>? customSnippets = null)
    {
        var prefix = context.CurrentWord.Trim();
        var results = new List<CompletionItem>();

        // Scenario 1: User typed after a dot (e.g. "c." or "cheeses.")
        if (context.IsAfterDot && !string.IsNullOrWhiteSpace(context.PrecedingIdentifier))
        {
            string preceding = context.PrecedingIdentifier;
            string? targetTable = null;

            if (context.TableAliases.TryGetValue(preceding, out var mappedTable))
            {
                targetTable = mappedTable;
            }
            else
            {
                var directMatch = tables.FirstOrDefault(t => string.Equals(t.Name, preceding, StringComparison.OrdinalIgnoreCase));
                if (directMatch != null)
                {
                    targetTable = directMatch.Name;
                }
            }

            if (targetTable != null && tableColumns != null)
            {
                // Find matching column list (case-insensitive dictionary lookup)
                var matchedKvp = tableColumns.FirstOrDefault(kvp => string.Equals(kvp.Key, targetTable, StringComparison.OrdinalIgnoreCase));
                if (matchedKvp.Value != null)
                {
                    foreach (var col in matchedKvp.Value)
                    {
                        if (Matches(col.Name, prefix))
                        {
                            results.Add(new CompletionItem
                            {
                                Label = col.Name,
                                Kind = CompletionItemKind.Column,
                                Detail = string.IsNullOrEmpty(col.DataType) ? "Column" : $"{col.DataType}{(col.IsPrimaryKey ? " [PK]" : "")}",
                                InsertText = col.Name,
                                Priority = CalculateScore(col.Name, prefix, 100)
                            });
                        }
                    }

                    return results.OrderByDescending(r => r.Priority).ThenBy(r => r.Label).ToList();
                }
            }

            return Array.Empty<CompletionItem>();
        }

        // Scenario 2: Standard completion (Keywords, Tables, Columns, Snippets)
        bool inTableContext = context.ActiveClause is "FROM" or "JOIN" or "INTO" or "UPDATE";

        // A. Tables & Views
        int tableBasePriority = inTableContext ? 95 : 55;
        foreach (var tbl in tables)
        {
            if (Matches(tbl.Name, prefix))
            {
                results.Add(new CompletionItem
                {
                    Label = tbl.Name,
                    Kind = tbl.IsView ? CompletionItemKind.View : CompletionItemKind.Table,
                    Detail = $"{tbl.Type} ({tbl.Schema})",
                    InsertText = tbl.Name,
                    Priority = CalculateScore(tbl.Name, prefix, tableBasePriority)
                });
            }
        }

        // B. Keywords
        foreach (var kw in SqlKeywords.Items)
        {
            if (Matches(kw.Label, prefix))
            {
                results.Add(new CompletionItem
                {
                    Label = kw.Label,
                    Kind = CompletionItemKind.Keyword,
                    Detail = kw.Detail,
                    InsertText = kw.InsertText,
                    Priority = CalculateScore(kw.Label, prefix, inTableContext ? 50 : 75)
                });
            }
        }

        // C. Snippets (Built-in & Custom)
        foreach (var snp in SqlSnippetCatalog.Items)
        {
            if (Matches(snp.Label, prefix))
            {
                results.Add(new CompletionItem
                {
                    Label = snp.Label,
                    Kind = CompletionItemKind.Snippet,
                    Detail = snp.Detail,
                    InsertText = snp.InsertText,
                    Priority = CalculateScore(snp.Label, prefix, 70)
                });
            }
        }

        if (customSnippets != null)
        {
            foreach (var snp in customSnippets)
            {
                if (Matches(snp.Prefix, prefix) || Matches(snp.Title, prefix))
                {
                    results.Add(new CompletionItem
                    {
                        Label = snp.Prefix,
                        Kind = CompletionItemKind.Snippet,
                        Detail = string.IsNullOrWhiteSpace(snp.Description) ? snp.Title : $"{snp.Title} ({snp.Description})",
                        InsertText = snp.Body,
                        Priority = CalculateScore(snp.Prefix, prefix, 85)
                    });
                }
            }
        }

        // D. Contextual columns from referenced tables
        if (!inTableContext && tableColumns != null)
        {
            var targetTables = context.ReferencedTables.Count > 0
                ? context.ReferencedTables
                : tables.Select(t => t.Name).ToList();

            foreach (var tableName in targetTables)
            {
                var matchedKvp = tableColumns.FirstOrDefault(kvp => string.Equals(kvp.Key, tableName, StringComparison.OrdinalIgnoreCase));
                if (matchedKvp.Value != null)
                {
                    foreach (var col in matchedKvp.Value)
                    {
                        if (Matches(col.Name, prefix))
                        {
                            results.Add(new CompletionItem
                            {
                                Label = col.Name,
                                Kind = CompletionItemKind.Column,
                                Detail = $"{col.DataType} ({tableName})",
                                InsertText = col.Name,
                                Priority = CalculateScore(col.Name, prefix, 60)
                            });
                        }
                    }
                }
            }
        }

        // Deduplicate identical items, sort by score, take top 50
        return results
            .GroupBy(r => (r.Label, r.Kind))
            .Select(g => g.First())
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Label)
            .Take(50)
            .ToList();
    }

    private static bool Matches(string candidate, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return true;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
               candidate.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int CalculateScore(string candidate, string prefix, int baseScore)
    {
        if (string.IsNullOrEmpty(prefix)) return baseScore;

        if (string.Equals(candidate, prefix, StringComparison.OrdinalIgnoreCase))
            return baseScore + 20;

        if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return baseScore + 10;

        return baseScore;
    }
}
