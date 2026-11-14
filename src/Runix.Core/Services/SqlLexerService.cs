namespace Runix.Core.Services;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Runix.Core.Models;

public partial class SqlLexerService : ISqlLexerService
{
    private static readonly HashSet<string> ReservedNonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "FULL", "CROSS", "OUTER", "NATURAL",
        "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "UNION", "SELECT", "SET", "AND", "OR", "NOT", "AS", "USING"
    };

    private static readonly string[] ClauseKeywords = new[]
    {
        "SELECT", "FROM", "WHERE", "JOIN", "ON", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "SET", "INTO", "VALUES", "UPDATE", "INSERT"
    };

    private static readonly Regex TableAliasRegex = new(
        @"\b(?:FROM|JOIN)\s+[""`\[]?([a-zA-Z0-9_]+)[""`\]]?(?:\s+(?:AS\s+)?[""`\[]?([a-zA-Z0-9_]+)[""`\]]?)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UpdateInsertRegex = new(
        @"\b(?:UPDATE|INSERT\s+INTO)\s+[""`\[]?([a-zA-Z0-9_]+)[""`\]]?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SqlContextInfo AnalyzeContext(string sqlText, int caretIndex)
    {
        if (string.IsNullOrWhiteSpace(sqlText) || caretIndex < 0)
        {
            return new SqlContextInfo();
        }

        if (caretIndex > sqlText.Length)
        {
            caretIndex = sqlText.Length;
        }

        // 1. Extract CurrentWord at caret
        int wordEnd = caretIndex;
        int wordStart = wordEnd;
        while (wordStart > 0 && IsWordChar(sqlText[wordStart - 1]))
        {
            wordStart--;
        }

        string currentWord = sqlText.Substring(wordStart, wordEnd - wordStart);

        // 2. Check for preceding dot and identifier (e.g. "c.name" or "c.")
        string? precedingIdentifier = null;
        int checkIndex = wordStart;

        // Skip whitespace before current word if any
        while (checkIndex > 0 && char.IsWhiteSpace(sqlText[checkIndex - 1]))
        {
            checkIndex--;
        }

        if (checkIndex > 0 && sqlText[checkIndex - 1] == '.')
        {
            int dotIndex = checkIndex - 1;
            int identEnd = dotIndex;

            // Skip whitespace before dot if any
            while (identEnd > 0 && char.IsWhiteSpace(sqlText[identEnd - 1]))
            {
                identEnd--;
            }

            int identStart = identEnd;
            while (identStart > 0 && IsWordChar(sqlText[identStart - 1]))
            {
                identStart--;
            }

            if (identEnd > identStart)
            {
                precedingIdentifier = sqlText.Substring(identStart, identEnd - identStart);
            }
        }

        // 3. Extract Table Aliases and Referenced Tables
        var tableAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var referencedTables = new List<string>();

        foreach (Match match in TableAliasRegex.Matches(sqlText))
        {
            if (match.Groups[1].Success)
            {
                var tableName = match.Groups[1].Value;
                if (!referencedTables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                {
                    referencedTables.Add(tableName);
                }

                // If alias provided
                if (match.Groups[2].Success)
                {
                    var alias = match.Groups[2].Value;
                    if (!ReservedNonAliases.Contains(alias))
                    {
                        tableAliases[alias] = tableName;
                    }
                }

                // Register table name itself as alias target so table.column works
                tableAliases[tableName] = tableName;
            }
        }

        foreach (Match match in UpdateInsertRegex.Matches(sqlText))
        {
            if (match.Groups[1].Success)
            {
                var tbl = match.Groups[1].Value;
                if (!referencedTables.Contains(tbl, StringComparer.OrdinalIgnoreCase))
                {
                    referencedTables.Add(tbl);
                }
                tableAliases[tbl] = tbl;
            }
        }

        // 4. Identify ActiveClause
        string? activeClause = FindActiveClause(sqlText, wordStart);

        return new SqlContextInfo
        {
            CurrentWord = currentWord,
            PrecedingIdentifier = precedingIdentifier,
            ActiveClause = activeClause,
            TableAliases = tableAliases,
            ReferencedTables = referencedTables
        };
    }

    private static string? FindActiveClause(string text, int endIndex)
    {
        string sub = text.Substring(0, endIndex);

        int lastPos = -1;
        string? foundClause = null;

        foreach (var clause in ClauseKeywords)
        {
            var pattern = $@"\b{Regex.Escape(clause)}\b";
            var matches = Regex.Matches(sub, pattern, RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                var lastMatch = matches[^1];
                if (lastMatch.Index > lastPos)
                {
                    lastPos = lastMatch.Index;
                    foundClause = clause.ToUpperInvariant();
                }
            }
        }

        return foundClause;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
