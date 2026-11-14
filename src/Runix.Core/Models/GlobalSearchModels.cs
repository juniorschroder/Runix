using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

public enum CellValueCategory
{
    Null,
    Scalar,
    Json,
    LongText,
    HexBlob
}

public sealed class InspectedCellField
{
    public int ColumnIndex { get; init; }
    public string ColumnName { get; init; } = string.Empty;
    public object? RawValue { get; init; }
    public string DisplayPreview { get; init; } = string.Empty;
    public string FormattedDetail { get; init; } = string.Empty;
    public CellValueCategory Category { get; init; }
    public int ByteOrCharLength { get; init; }

    public string CategoryBadge => Category switch
    {
        CellValueCategory.Null => "NULL",
        CellValueCategory.Json => "JSON",
        CellValueCategory.HexBlob => "BLOB/HEX",
        CellValueCategory.LongText => "TEXT",
        _ => "VAL"
    };

    public string CategoryColor => Category switch
    {
        CellValueCategory.Null => "#64748b",
        CellValueCategory.Json => "#fbbf24",
        CellValueCategory.HexBlob => "#c084fc",
        CellValueCategory.LongText => "#38bdf8",
        _ => "#34d399"
    };
}

public sealed class GlobalSearchOptions
{
    public string SearchTerm { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public int MaxRowsPerTable { get; set; } = 25;
    public HashSet<string>? IncludedTables { get; set; }
}

public sealed class GlobalSearchTableMatch
{
    public string Schema { get; init; } = "main";
    public string TableName { get; init; } = string.Empty;
    public int MatchedRowCount { get; init; }
    public List<string> MatchedColumns { get; init; } = new();
    public QueryResult SampleResult { get; init; } = new();
    public string FilterSqlClause { get; init; } = string.Empty;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Schema) || string.Equals(Schema, "main", StringComparison.OrdinalIgnoreCase)
        ? TableName
        : $"{Schema}.{TableName}";

    public string MatchedColumnsSummary => MatchedColumns.Count > 0
        ? $"Colunas: {string.Join(", ", MatchedColumns)}"
        : string.Empty;
}

public sealed class GlobalSearchReport
{
    public string SearchTerm { get; init; } = string.Empty;
    public int TablesScanned { get; init; }
    public int TablesWithMatches => Matches.Count;
    public int TotalMatchedRows { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public List<GlobalSearchTableMatch> Matches { get; init; } = new();
}
