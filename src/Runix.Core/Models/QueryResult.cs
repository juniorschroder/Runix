using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

public class QueryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
    public int AffectedRows { get; set; }
    public int RowsAffected { get => AffectedRows; set => AffectedRows = value; }
    public double ElapsedMilliseconds { get; set; }
    public string? ExecutedSql { get; set; }
    public string? Title { get; set; }
    public List<QueryResult> SubResults { get; set; } = new();
    public bool HasMultipleResults => SubResults.Count > 1;

    public static QueryResult CreateSuccess(List<string> columns, List<List<object?>> rows, double elapsedMs, int affected = 0)
    {
        return new QueryResult
        {
            Success = true,
            Columns = columns,
            Rows = rows,
            ElapsedMilliseconds = elapsedMs,
            AffectedRows = affected
        };
    }

    public static QueryResult CreateError(string error, double elapsedMs = 0)
    {
        return new QueryResult
        {
            Success = false,
            ErrorMessage = error,
            ElapsedMilliseconds = elapsedMs
        };
    }
}

public class QueryHistoryItem
{
    public string Sql { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.Now;
    public DateTime Timestamp { get => ExecutedAt; set => ExecutedAt = value; }
    public bool Success { get; set; }
    public int RowCount { get; set; }
    public double DurationMs { get; set; }
    public string FormattedTime => ExecutedAt.ToString("HH:mm:ss");
    public string Summary => $"{FormattedTime} - {(Success ? $"{RowCount} rows" : "Failed")} ({DurationMs:F0}ms)";
    public string DisplaySummary => $"[{ExecutedAt:HH:mm:ss}] {(Success ? $"{RowCount} rows" : "Failed")} in {DurationMs:F1}ms - {Sql.Trim().Replace('\n', ' ')}";
}
