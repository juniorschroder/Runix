namespace Runix.Core.Services;

using System;
using System.Collections.Generic;
using Runix.Core.Models;

public static class SqlKeywords
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        // Query clauses & structure
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "FULL JOIN", "CROSS JOIN", "ON",
        "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "UNION", "UNION ALL", "DISTINCT", "AS",
        
        // Data manipulation (DML)
        "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
        
        // Data definition (DDL)
        "CREATE TABLE", "ALTER TABLE", "DROP TABLE", "CREATE INDEX", "DROP INDEX", "CREATE VIEW", "DROP VIEW",
        "PRIMARY KEY", "FOREIGN KEY", "REFERENCES", "CONSTRAINT", "UNIQUE", "CHECK", "DEFAULT", "CASCADE",
        
        // Logical & comparison operators
        "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "ILIKE", "IS NULL", "IS NOT NULL", "EXISTS",
        
        // Conditionals
        "CASE", "WHEN", "THEN", "ELSE", "END",
        
        // Ordering & modifiers
        "ASC", "DESC", "NULLS FIRST", "NULLS LAST", "TRUE", "FALSE", "NULL",
        
        // Common aggregate & scalar functions
        "COUNT(*)", "SUM", "AVG", "MIN", "MAX", "COALESCE", "ROUND", "CAST", "CONCAT", "LENGTH", "UPPER", "LOWER", "TRIM"
    };

    public static readonly IReadOnlyList<CompletionItem> Items = CreateCompletionItems();

    private static IReadOnlyList<CompletionItem> CreateCompletionItems()
    {
        var list = new List<CompletionItem>(All.Count);
        foreach (var kw in All)
        {
            list.Add(new CompletionItem
            {
                Label = kw,
                Kind = CompletionItemKind.Keyword,
                Detail = "SQL Keyword",
                InsertText = kw.EndsWith(")") ? kw : $"{kw} ",
                Priority = 70
            });
        }
        return list;
    }
}
