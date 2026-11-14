using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Database;

public sealed class GlobalDataSearchService : IGlobalDataSearchService
{
    private readonly IDatabaseService _databaseService;

    public GlobalDataSearchService(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public async Task<GlobalSearchReport> SearchAcrossDatabaseAsync(
        GlobalSearchOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options == null || string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            return new GlobalSearchReport();
        }

        var sw = Stopwatch.StartNew();
        string term = options.SearchTerm.Trim();
        int maxRows = Math.Clamp(options.MaxRowsPerTable <= 0 ? 25 : options.MaxRowsPerTable, 1, 250);
        bool isPostgres = _databaseService.Config.Type == DatabaseType.PostgreSQL;

        var tables = await _databaseService.GetTablesAsync();
        if (options.IncludedTables is { Count: > 0 })
        {
            tables = tables
                .Where(t => options.IncludedTables.Contains(t.Name))
                .ToList();
        }

        var matches = new List<GlobalSearchTableMatch>();
        int tablesScanned = 0;
        int totalMatchedRows = 0;

        string escapedTerm = EscapeLikeLiteral(term);

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tablesScanned++;
            progress?.Report($"Varrendo {table.Name} ({tablesScanned}/{tables.Count})...");

            List<ColumnInfo> cols;
            try
            {
                cols = await _databaseService.GetTableColumnsAsync(table.Name);
            }
            catch
            {
                continue;
            }

            if (cols == null || cols.Count == 0)
            {
                continue;
            }

            var predicates = new List<string>(cols.Count);
            foreach (var col in cols)
            {
                string qCol = QuoteIdentifier(col.Name);
                if (isPostgres)
                {
                    string op = options.CaseSensitive ? "LIKE" : "ILIKE";
                    predicates.Add($"CAST({qCol} AS TEXT) {op} '%{escapedTerm}%'");
                }
                else
                {
                    if (options.CaseSensitive)
                    {
                        predicates.Add($"INSTR(CAST({qCol} AS TEXT), '{EscapeSqlString(term)}') > 0");
                    }
                    else
                    {
                        predicates.Add($"LOWER(CAST({qCol} AS TEXT)) LIKE LOWER('%{escapedTerm}%')");
                    }
                }
            }

            string whereClause = string.Join(" OR ", predicates);
            string qualifiedTable = QualifyTable(table.Schema, table.Name, isPostgres);
            string sql = $"SELECT * FROM {qualifiedTable} WHERE {whereClause} LIMIT {maxRows};";

            var queryResult = await _databaseService.ExecuteQueryAsync(sql);
            if (!queryResult.Success || queryResult.Rows.Count == 0)
            {
                continue;
            }

            // Identify which columns matched in the sample rows
            var matchedCols = new List<string>();
            for (int cIdx = 0; cIdx < queryResult.Columns.Count; cIdx++)
            {
                string colName = queryResult.Columns[cIdx];
                bool colHasMatch = queryResult.Rows.Any(r =>
                {
                    if (cIdx >= r.Count || r[cIdx] is null)
                    {
                        return false;
                    }

                    string cellText = r[cIdx]?.ToString() ?? string.Empty;
                    return cellText.Contains(
                        term,
                        options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                });

                if (colHasMatch)
                {
                    matchedCols.Add(colName);
                }
            }

            int rowCount = queryResult.Rows.Count;
            totalMatchedRows += rowCount;

            matches.Add(new GlobalSearchTableMatch
            {
                Schema = table.Schema,
                TableName = table.Name,
                MatchedRowCount = rowCount,
                MatchedColumns = matchedCols,
                SampleResult = queryResult,
                FilterSqlClause = whereClause
            });
        }

        sw.Stop();
        progress?.Report($"Concluído: {totalMatchedRows} registros em {matches.Count} tabela(s) ({sw.Elapsed.TotalMilliseconds:F0} ms).");

        return new GlobalSearchReport
        {
            SearchTerm = term,
            TablesScanned = tablesScanned,
            TotalMatchedRows = totalMatchedRows,
            ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
            Matches = matches
        };
    }

    private static string QualifyTable(string? schema, string tableName, bool isPostgres)
    {
        if (isPostgres && !string.IsNullOrWhiteSpace(schema) && !string.Equals(schema, "main", StringComparison.OrdinalIgnoreCase))
        {
            return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
        }
        return QuoteIdentifier(tableName);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{(identifier ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    private static string EscapeSqlString(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    private static string EscapeLikeLiteral(string value)
    {
        return (value ?? string.Empty)
            .Replace("'", "''");
    }
}
