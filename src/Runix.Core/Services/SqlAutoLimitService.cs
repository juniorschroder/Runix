namespace Runix.Core.Services;

using System;
using System.Text.RegularExpressions;
using Runix.Core.Interfaces;

/// <summary>
/// Intelligent SQL auto-limiter that prevents accidental full-table scans by appending LIMIT {n}
/// to unbounded SELECT and CTE queries.
/// </summary>
public class SqlAutoLimitService : ISqlAutoLimitService
{
    private static readonly Lazy<SqlAutoLimitService> _instance = new(() => new SqlAutoLimitService());
    public static SqlAutoLimitService Instance => _instance.Value;

    private static readonly Regex SelectOrWithRegex = new(
        @"^\s*(WITH\b[\s\S]+?\bSELECT\b|SELECT\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExistingLimitRegex = new(
        @"\b(LIMIT\s+\d+|FETCH\s+(FIRST|NEXT)\s+\d+\s+ROWS?\s+ONLY)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public (string Sql, bool Applied) ApplyAutoLimit(string sql, int limit)
    {
        if (string.IsNullOrWhiteSpace(sql) || limit <= 0)
        {
            return (sql, false);
        }

        var trimmed = sql.Trim();
        bool hasTrailingSemicolon = trimmed.EndsWith(';');
        if (hasTrailingSemicolon)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
        }

        // If there are multiple statements (separated by semicolon outside quotes), do not auto-limit
        var statements = SqlSafetyService.SplitStatements(trimmed);
        if (statements.Count != 1)
        {
            return (sql, false);
        }

        var clean = SqlSafetyService.StripComments(trimmed);
        if (!SelectOrWithRegex.IsMatch(clean))
        {
            return (sql, false);
        }

        if (ExistingLimitRegex.IsMatch(clean))
        {
            return (sql, false);
        }

        var modified = $"{trimmed} LIMIT {limit}" + (hasTrailingSemicolon ? ";" : "");
        return (modified, true);
    }
}
