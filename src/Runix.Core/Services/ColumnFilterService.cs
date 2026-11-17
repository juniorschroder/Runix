using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Runix.Core.Services;

public class ColumnFilterService : IColumnFilterService
{
    private static readonly Lazy<ColumnFilterService> _instance = new(() => new ColumnFilterService());
    public static ColumnFilterService Instance => _instance.Value;

    public string? BuildPredicate(string columnName, string rawInput)
    {
        if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(rawInput))
            return null;

        var safeCol = $"\"{columnName.Trim().Replace("\"", "\"\"")}\"";
        var trimmed = rawInput.Trim();

        // 1. IS NULL / NULL
        if (string.Equals(trimmed, "IS NULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return $"{safeCol} IS NULL";
        }

        // 2. IS NOT NULL / NOT NULL
        if (string.Equals(trimmed, "IS NOT NULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NOT NULL", StringComparison.OrdinalIgnoreCase))
        {
            return $"{safeCol} IS NOT NULL";
        }

        // 3. IN (...)
        if (trimmed.StartsWith("IN", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('(') && trimmed.EndsWith(')'))
        {
            return $"{safeCol} {trimmed}";
        }

        // 4. BETWEEN ... AND ...
        if (trimmed.StartsWith("BETWEEN", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("AND", StringComparison.OrdinalIgnoreCase))
        {
            return $"{safeCol} {trimmed}";
        }

        // 5. LIKE, ILIKE, NOT LIKE
        if (trimmed.StartsWith("NOT LIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = FormatLikePattern(trimmed.Substring(9));
            return $"{safeCol} NOT LIKE {pattern}";
        }
        if (trimmed.StartsWith("LIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = FormatLikePattern(trimmed.Substring(5));
            return $"{safeCol} LIKE {pattern}";
        }
        if (trimmed.StartsWith("ILIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = FormatLikePattern(trimmed.Substring(6));
            return $"{safeCol} ILIKE {pattern}";
        }

        // 6. Comparison Operators: >=, <=, !=, <>, >, <, =
        string[] operators = { ">=", "<=", "!=", "<>", ">", "<", "=" };
        foreach (var op in operators)
        {
            if (trimmed.StartsWith(op))
            {
                var valPart = trimmed.Substring(op.Length).Trim();
                var sqlOp = op == "!=" ? "<>" : op;
                var formattedVal = FormatValueLiteral(valPart);
                return $"{safeCol} {sqlOp} {formattedVal}";
            }
        }

        // 7. Plain text without operator
        // If numeric, default to exact equality
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return $"{safeCol} = {trimmed}";
        }

        // If quoted string, exact equality
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
        {
            return $"{safeCol} = {trimmed}";
        }

        // If boolean keywords
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return $"{safeCol} = {trimmed.ToUpperInvariant()}";
        }

        // Otherwise: wildcard substring search
        var escapedText = trimmed.Replace("'", "''");
        if (escapedText.Contains('%') || escapedText.Contains('_'))
        {
            return $"{safeCol} LIKE '{escapedText}'";
        }

        return $"{safeCol} LIKE '%{escapedText}%'";
    }

    public string BuildWhereClause(IReadOnlyDictionary<string, string> columnFilters)
    {
        if (columnFilters == null || columnFilters.Count == 0)
            return string.Empty;

        var predicates = new List<string>();

        foreach (var (col, filter) in columnFilters)
        {
            var predicate = BuildPredicate(col, filter);
            if (!string.IsNullOrWhiteSpace(predicate))
            {
                predicates.Add($"({predicate})");
            }
        }

        return predicates.Count > 0 ? string.Join(" AND ", predicates) : string.Empty;
    }

    public string BuildOrderByClause(IReadOnlyList<(string Column, bool Ascending)> sortOrders)
    {
        if (sortOrders == null || sortOrders.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var (col, asc) in sortOrders)
        {
            if (string.IsNullOrWhiteSpace(col)) continue;
            var safeCol = $"\"{col.Trim().Replace("\"", "\"\"")}\"";
            parts.Add($"{safeCol} {(asc ? "ASC" : "DESC")}");
        }

        return string.Join(", ", parts);
    }

    private static string FormatLikePattern(string rawPattern)
    {
        var trimmed = rawPattern.Trim();
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
            return trimmed;

        var escaped = trimmed.Replace("'", "''");
        if (!escaped.Contains('%') && !escaped.Contains('_'))
        {
            return $"'%{escaped}%'";
        }
        return $"'{escaped}'";
    }

    private static string FormatValueLiteral(string rawVal)
    {
        var trimmed = rawVal.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "''";

        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
            return trimmed;

        if (string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
            return "NULL";
        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase))
            return "TRUE";
        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
            return "FALSE";

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
        }

        return $"'{trimmed.Replace("'", "''")}'";
    }

    public bool MatchesValue(object? cellValue, string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return true;

        var trimmed = rawInput.Trim();

        // 1. IS NULL
        if (string.Equals(trimmed, "IS NULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return cellValue == null || cellValue == DBNull.Value;
        }

        // 2. IS NOT NULL
        if (string.Equals(trimmed, "IS NOT NULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NOT NULL", StringComparison.OrdinalIgnoreCase))
        {
            return cellValue != null && cellValue != DBNull.Value;
        }

        if (cellValue == null || cellValue == DBNull.Value)
            return false;

        var strVal = cellValue.ToString() ?? "";

        // 3. Comparison operators: >=, <=, !=, <>, >, <, =
        string[] ops = { ">=", "<=", "!=", "<>", ">", "<", "=" };
        foreach (var op in ops)
        {
            if (trimmed.StartsWith(op))
            {
                var targetStr = trimmed.Substring(op.Length).Trim().Trim('\'');
                return CompareOperator(strVal, op, targetStr);
            }
        }

        // 4. LIKE / ILIKE / NOT LIKE
        if (trimmed.StartsWith("NOT LIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = trimmed.Substring(9).Trim().Trim('\'', '%');
            return !strVal.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
        if (trimmed.StartsWith("LIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = trimmed.Substring(5).Trim().Trim('\'', '%');
            return strVal.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
        if (trimmed.StartsWith("ILIKE ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = trimmed.Substring(6).Trim().Trim('\'', '%');
            return strVal.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        // 5. Se for número e o alvo for número
        if (double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal) &&
            double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var numTarget))
        {
            return Math.Abs(numVal - numTarget) < 0.0000001;
        }

        // 6. Contém texto (case-insensitive substring)
        var cleanTarget = trimmed.Trim('\'', '%');
        return strVal.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareOperator(string strVal, string op, string targetStr)
    {
        if (double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var numCell) &&
            double.TryParse(targetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var numTarget))
        {
            return op switch
            {
                ">=" => numCell >= numTarget,
                "<=" => numCell <= numTarget,
                "!=" or "<>" => Math.Abs(numCell - numTarget) > 0.0000001,
                ">" => numCell > numTarget,
                "<" => numCell < numTarget,
                "=" => Math.Abs(numCell - numTarget) < 0.0000001,
                _ => false
            };
        }

        var cmp = string.Compare(strVal, targetStr, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            "!=" or "<>" => cmp != 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            "=" => cmp == 0,
            _ => false
        };
    }
}
