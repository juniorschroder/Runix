using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Core.Services;

public sealed class CommandPaletteService : ICommandPaletteService
{
    private readonly List<CommandPaletteItem> _catalog = new();
    private readonly List<string> _mruIds = new();
    private const int MaxMruHistory = 25;

    public void SetCatalog(IEnumerable<CommandPaletteItem> items)
    {
        _catalog.Clear();
        if (items != null)
        {
            _catalog.AddRange(items);
        }
    }

    public void RebuildCatalog(
        IEnumerable<CommandPaletteItem> systemActions,
        IEnumerable<TableInfo>? tables = null,
        IEnumerable<DatabaseObjectInfo>? programmableObjects = null,
        IEnumerable<SavedQuery>? savedQueries = null,
        IEnumerable<CustomSnippet>? snippets = null,
        ISet<string>? favoriteTables = null)
    {
        _catalog.Clear();

        if (systemActions != null)
        {
            _catalog.AddRange(systemActions);
        }

        if (tables != null)
        {
            foreach (var table in tables)
            {
                bool isFav = favoriteTables != null && favoriteTables.Contains(table.Name);
                bool isView = table.IsView;
                string schemaPrefix = !string.IsNullOrWhiteSpace(table.Schema) && !string.Equals(table.Schema, "main", StringComparison.OrdinalIgnoreCase)
                    ? $"{table.Schema}."
                    : string.Empty;

                _catalog.Add(new CommandPaletteItem
                {
                    Id = $"{(isView ? "view" : "table")}:{schemaPrefix}{table.Name}",
                    Title = $"{schemaPrefix}{table.Name}",
                    Subtitle = isView
                        ? "View • (Enter: Abrir Dados | Ctrl+Enter: Editar DDL)"
                        : $"Tabela • {(table.RowCount.HasValue ? $"{table.RowCount.Value:N0} linhas" : "Abrir dados")} (Enter: Dados | Ctrl+Enter: Estrutura)",
                    Category = isView ? CommandPaletteCategory.View : CommandPaletteCategory.Table,
                    Icon = isFav ? "⭐" : (isView ? "👁️" : "🗄"),
                    ShortcutHint = "Enter / Ctrl+Enter",
                    Payload = table,
                    IsFavorite = isFav
                });
            }
        }

        if (programmableObjects != null)
        {
            foreach (var obj in programmableObjects)
            {
                if (obj.ObjectType == DatabaseObjectType.View && tables != null &&
                    tables.Any(t => t.IsView && string.Equals(t.Name, obj.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string icon = obj.ObjectType switch
                {
                    DatabaseObjectType.View => "👁️",
                    DatabaseObjectType.Trigger => "⚡",
                    DatabaseObjectType.Function => "ƒ",
                    DatabaseObjectType.Procedure => "⚙️",
                    DatabaseObjectType.Index => "📇",
                    _ => "📝"
                };

                _catalog.Add(new CommandPaletteItem
                {
                    Id = $"ddl:{obj.ObjectType}:{obj.Schema}.{obj.Name}",
                    Title = obj.Name,
                    Subtitle = $"{obj.ObjectType} {(string.IsNullOrWhiteSpace(obj.TargetTableName) ? string.Empty : $"em {obj.TargetTableName}")} • Editar Definição DDL",
                    Category = CommandPaletteCategory.ProgrammableObject,
                    Icon = icon,
                    ShortcutHint = "DDL",
                    Payload = obj
                });
            }
        }

        if (savedQueries != null)
        {
            foreach (var query in savedQueries)
            {
                _catalog.Add(new CommandPaletteItem
                {
                    Id = $"query:{query.Id}",
                    Title = query.Title,
                    Subtitle = string.IsNullOrWhiteSpace(query.TagsSummary)
                        ? TruncateOneLine(query.SqlText, 70)
                        : $"[{query.TagsSummary}] {TruncateOneLine(query.SqlText, 55)}",
                    Category = CommandPaletteCategory.SavedQuery,
                    Icon = query.IsFavorite ? "⭐" : "📄",
                    ShortcutHint = "Abrir SQL",
                    Payload = query,
                    IsFavorite = query.IsFavorite
                });
            }
        }

        if (snippets != null)
        {
            foreach (var snippet in snippets)
            {
                _catalog.Add(new CommandPaletteItem
                {
                    Id = $"snippet:{snippet.Prefix}",
                    Title = $"{snippet.Prefix} — {snippet.Title}",
                    Subtitle = TruncateOneLine(snippet.Body, 70),
                    Category = CommandPaletteCategory.Snippet,
                    Icon = "✂️",
                    ShortcutHint = "Inserir Snippet",
                    Payload = snippet
                });
            }
        }
    }

    public void RecordUsage(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        _mruIds.RemoveAll(id => string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase));
        _mruIds.Insert(0, itemId);

        if (_mruIds.Count > MaxMruHistory)
        {
            _mruIds.RemoveAt(_mruIds.Count - 1);
        }
    }

    public IReadOnlyList<CommandPaletteItem> Search(string? rawQuery, int maxResults = 35)
    {
        string trimmed = (rawQuery ?? string.Empty).Trim();
        Func<CommandPaletteItem, bool> categoryFilter = _ => true;

        if (trimmed.StartsWith('>'))
        {
            categoryFilter = item => item.Category == CommandPaletteCategory.Action;
            trimmed = trimmed[1..].Trim();
        }
        else if (trimmed.StartsWith('#'))
        {
            categoryFilter = item => item.Category is CommandPaletteCategory.Table or CommandPaletteCategory.View;
            trimmed = trimmed[1..].Trim();
        }
        else if (trimmed.StartsWith('@'))
        {
            categoryFilter = item => item.Category is CommandPaletteCategory.ProgrammableObject or CommandPaletteCategory.View;
            trimmed = trimmed[1..].Trim();
        }
        else if (trimmed.StartsWith('?'))
        {
            categoryFilter = item => item.Category is CommandPaletteCategory.SavedQuery or CommandPaletteCategory.Snippet;
            trimmed = trimmed[1..].Trim();
        }

        var filtered = _catalog.Where(categoryFilter);

        if (string.IsNullOrEmpty(trimmed))
        {
            return filtered
                .Select(item => new CommandPaletteScoredItem(item, GetDefaultScore(item)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(x => x.Item)
                .ToList();
        }

        var scored = new List<CommandPaletteScoredItem>();
        foreach (var item in filtered)
        {
            int score = ComputeFuzzyScore(item, trimmed);
            if (score > 0)
            {
                scored.Add(new CommandPaletteScoredItem(item, score));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Title.Length)
            .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(x => x.Item)
            .ToList();
    }

    private int GetDefaultScore(CommandPaletteItem item)
    {
        int score = 100;
        int mruIndex = _mruIds.FindIndex(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (mruIndex >= 0)
        {
            score += Math.Max(30, 300 - (mruIndex * 12));
        }

        if (item.IsFavorite)
        {
            score += 90;
        }

        score += item.Category switch
        {
            CommandPaletteCategory.Action => 50,
            CommandPaletteCategory.Table => 40,
            CommandPaletteCategory.View => 35,
            CommandPaletteCategory.SavedQuery => 30,
            CommandPaletteCategory.ProgrammableObject => 25,
            CommandPaletteCategory.Snippet => 20,
            _ => 10
        };

        return score;
    }

    public int ComputeFuzzyScore(CommandPaletteItem item, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return GetDefaultScore(item);
        }

        string title = item.Title ?? string.Empty;
        string subtitle = item.Subtitle ?? string.Empty;

        int baseScore = 0;

        if (string.Equals(title, pattern, StringComparison.OrdinalIgnoreCase))
        {
            baseScore = 1000;
        }
        else if (title.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            baseScore = 700;
        }
        else if (MatchesInitialsOrAcronym(title, pattern))
        {
            baseScore = 520;
        }
        else
        {
            int titleIdx = title.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (titleIdx >= 0)
            {
                baseScore = 400 - Math.Min(80, titleIdx * 3);
            }
            else
            {
                int subseqScore = ComputeSubsequenceScore(title, pattern);
                if (subseqScore > 0)
                {
                    baseScore = subseqScore;
                }
                else if (subtitle.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    baseScore = 160;
                }
            }
        }

        if (baseScore <= 0)
        {
            return 0;
        }

        int mruIndex = _mruIds.FindIndex(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (mruIndex >= 0)
        {
            baseScore += Math.Max(20, 180 - (mruIndex * 10));
        }

        if (item.IsFavorite)
        {
            baseScore += 35;
        }

        return baseScore;
    }

    private static bool MatchesInitialsOrAcronym(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        // Normalize pattern removing underscores/spaces so both "ur" and "u_r" match "user_roles"
        string cleanPattern = new string(pattern.Where(char.IsLetterOrDigit).ToArray());
        if (cleanPattern.Length < 2)
        {
            return false;
        }

        var initials = new List<char>();
        bool startOfWord = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsLetterOrDigit(c))
            {
                startOfWord = true;
                continue;
            }

            if (startOfWord || (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1])))
            {
                initials.Add(char.ToLowerInvariant(c));
                startOfWord = false;
            }
        }

        string acronym = new string(initials.ToArray());
        return acronym.StartsWith(cleanPattern, StringComparison.OrdinalIgnoreCase)
               || acronym.Contains(cleanPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static int ComputeSubsequenceScore(string text, string pattern)
    {
        int pIdx = 0;
        int firstMatch = -1;
        int lastMatch = -1;
        int consecutiveBonus = 0;

        for (int tIdx = 0; tIdx < text.Length && pIdx < pattern.Length; tIdx++)
        {
            if (char.ToLowerInvariant(text[tIdx]) == char.ToLowerInvariant(pattern[pIdx]))
            {
                if (firstMatch < 0)
                {
                    firstMatch = tIdx;
                }

                if (lastMatch == tIdx - 1)
                {
                    consecutiveBonus += 15;
                }

                lastMatch = tIdx;
                pIdx++;
            }
        }

        if (pIdx < pattern.Length)
        {
            return 0;
        }

        int span = lastMatch - firstMatch + 1;
        int gapPenalty = Math.Max(0, (span - pattern.Length) * 8);
        int raw = 220 + consecutiveBonus - gapPenalty;
        return Math.Max(40, raw);
    }

    private static string TruncateOneLine(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length <= maxLen ? oneLine : oneLine[..maxLen] + "…";
    }
}
