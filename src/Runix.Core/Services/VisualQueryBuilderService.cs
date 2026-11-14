using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Core.Services;

public class VisualQueryBuilderService : IVisualQueryBuilderService
{
    private readonly ISqlFormatterService _formatter;

    public VisualQueryBuilderService(ISqlFormatterService? formatter = null)
    {
        _formatter = formatter ?? new SqlFormatterService();
    }

    public string BuildSql(VisualQueryModel model, DatabaseType dialect = DatabaseType.SQLite)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.BaseTable))
            return "-- Selecione uma tabela base para construir a consulta.";

        var sb = new StringBuilder();
        var selectedCols = model.SelectColumns.Where(c => c.IsSelected && !string.IsNullOrWhiteSpace(c.ColumnName)).ToList();

        // 1. SELECT Clause
        sb.Append("SELECT ");
        if (model.Distinct)
            sb.Append("DISTINCT ");

        if (selectedCols.Count == 0)
        {
            sb.Append('*');
        }
        else
        {
            var selectParts = new List<string>(selectedCols.Count);
            foreach (var col in selectedCols)
            {
                var colRef = FormatColumnRef(col.TableAlias, col.ColumnName);
                var expr = !string.IsNullOrWhiteSpace(col.AggregateFunction) && col.AggregateFunction != "NONE"
                    ? $"{col.AggregateFunction.ToUpperInvariant()}({colRef})"
                    : colRef;

                if (!string.IsNullOrWhiteSpace(col.OutputAlias))
                {
                    expr += $" AS {QuoteIdent(col.OutputAlias)}";
                }
                selectParts.Add(expr);
            }
            sb.Append(string.Join(", ", selectParts));
        }

        // 2. FROM Clause
        sb.Append($" FROM {QuoteIdent(model.BaseTable)}");
        if (!string.IsNullOrWhiteSpace(model.BaseTableAlias) &&
            !string.Equals(model.BaseTableAlias, model.BaseTable, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" {model.BaseTableAlias.Trim()}");
        }

        // 3. JOIN Clauses
        foreach (var join in model.Joins.Where(j => !string.IsNullOrWhiteSpace(j.TargetTable)))
        {
            var jType = string.IsNullOrWhiteSpace(join.JoinType) ? "INNER JOIN" : join.JoinType.ToUpperInvariant();
            if (!jType.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase))
                jType += " JOIN";

            sb.Append($" {jType} {QuoteIdent(join.TargetTable)}");
            if (!string.IsNullOrWhiteSpace(join.TargetAlias) &&
                !string.Equals(join.TargetAlias, join.TargetTable, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append($" {join.TargetAlias.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(join.SourceColumn) && !string.IsNullOrWhiteSpace(join.TargetColumn))
            {
                var leftRef = FormatColumnRef(
                    string.IsNullOrWhiteSpace(join.SourceTableAlias) ? (string.IsNullOrWhiteSpace(model.BaseTableAlias) ? model.BaseTable : model.BaseTableAlias) : join.SourceTableAlias,
                    join.SourceColumn);
                var rightRef = FormatColumnRef(
                    string.IsNullOrWhiteSpace(join.TargetAlias) ? join.TargetTable : join.TargetAlias,
                    join.TargetColumn);
                var op = string.IsNullOrWhiteSpace(join.Operator) ? "=" : join.Operator.Trim();
                sb.Append($" ON {leftRef} {op} {rightRef}");
            }
        }

        // 4. WHERE Clause
        var validFilters = model.Filters.Where(f => !string.IsNullOrWhiteSpace(f.ColumnName)).ToList();
        if (validFilters.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < validFilters.Count; i++)
            {
                var f = validFilters[i];
                if (i > 0)
                {
                    var logic = string.Equals(f.LogicalOperator, "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";
                    sb.Append($" {logic} ");
                }

                var colRef = FormatColumnRef(f.TableAlias, f.ColumnName);
                var op = string.IsNullOrWhiteSpace(f.ComparisonOperator) ? "=" : f.ComparisonOperator.Trim().ToUpperInvariant();

                if (op == "ILIKE" && dialect == DatabaseType.SQLite)
                    op = "LIKE";

                if (op is "IS NULL" or "IS NOT NULL")
                {
                    sb.Append($"{colRef} {op}");
                }
                else if (op == "IN")
                {
                    var rawVal = f.Value.Trim();
                    if (!rawVal.StartsWith('('))
                        rawVal = $"({rawVal})";
                    sb.Append($"{colRef} IN {rawVal}");
                }
                else
                {
                    sb.Append($"{colRef} {op} {FormatFilterValue(f.Value)}");
                }
            }
        }

        // 5. GROUP BY Clause (automatic when mixing aggregated and non-aggregated columns)
        bool hasAggregate = selectedCols.Any(c => !string.IsNullOrWhiteSpace(c.AggregateFunction) && c.AggregateFunction != "NONE");
        var nonAggregated = selectedCols.Where(c => string.IsNullOrWhiteSpace(c.AggregateFunction) || c.AggregateFunction == "NONE").ToList();
        if (hasAggregate && nonAggregated.Count > 0)
        {
            var groupRefs = nonAggregated.Select(c => FormatColumnRef(c.TableAlias, c.ColumnName));
            sb.Append(" GROUP BY ").Append(string.Join(", ", groupRefs));
        }

        // 6. ORDER BY Clause
        var validSorts = model.Sorts.Where(s => !string.IsNullOrWhiteSpace(s.ColumnName)).ToList();
        if (validSorts.Count > 0)
        {
            var sortParts = validSorts.Select(s =>
            {
                var dir = string.Equals(s.Direction, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                return $"{FormatColumnRef(s.TableAlias, s.ColumnName)} {dir}";
            });
            sb.Append(" ORDER BY ").Append(string.Join(", ", sortParts));
        }

        // 7. LIMIT & OFFSET
        if (model.Limit.HasValue && model.Limit.Value > 0)
        {
            sb.Append($" LIMIT {model.Limit.Value}");
        }

        if (model.Offset.HasValue && model.Offset.Value > 0)
        {
            sb.Append($" OFFSET {model.Offset.Value}");
        }

        sb.Append(';');
        return _formatter.Format(sb.ToString());
    }

    public VisualJoinClause SuggestJoinClause(
        string baseTable,
        string baseAlias,
        string joinTable,
        string joinAlias,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var effectiveBaseAlias = string.IsNullOrWhiteSpace(baseAlias) ? baseTable : baseAlias;
        var effectiveJoinAlias = string.IsNullOrWhiteSpace(joinAlias) ? joinTable : joinAlias;

        var clause = new VisualJoinClause
        {
            JoinType = "INNER JOIN",
            TargetTable = joinTable,
            TargetAlias = joinAlias,
            SourceTableAlias = effectiveBaseAlias,
            Operator = "="
        };

        if (foreignKeys != null)
        {
            // Case 1: baseTable has a foreign key pointing to joinTable
            var fkForward = foreignKeys.FirstOrDefault(fk =>
                string.Equals(fk.SourceTable, baseTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.TargetTable, joinTable, StringComparison.OrdinalIgnoreCase));

            if (fkForward != null)
            {
                clause.SourceColumn = fkForward.SourceColumn;
                clause.TargetColumn = fkForward.TargetColumn;
                return clause;
            }

            // Case 2: joinTable has a foreign key pointing to baseTable
            var fkReverse = foreignKeys.FirstOrDefault(fk =>
                string.Equals(fk.SourceTable, joinTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.TargetTable, baseTable, StringComparison.OrdinalIgnoreCase));

            if (fkReverse != null)
            {
                clause.SourceColumn = fkReverse.TargetColumn;
                clause.TargetColumn = fkReverse.SourceColumn;
                return clause;
            }
        }

        // Fallback convention: id = <singular_base>_id
        var singularBase = baseTable.EndsWith("s", StringComparison.OrdinalIgnoreCase) && baseTable.Length > 1
            ? baseTable[..^1]
            : baseTable;

        clause.SourceColumn = "id";
        clause.TargetColumn = $"{singularBase}_id";
        return clause;
    }

    private static string FormatColumnRef(string? tableAlias, string columnName)
    {
        if (columnName == "*")
            return string.IsNullOrWhiteSpace(tableAlias) ? "*" : $"{tableAlias.Trim()}.*";

        var quotedCol = QuoteIdent(columnName);
        return string.IsNullOrWhiteSpace(tableAlias)
            ? quotedCol
            : $"{tableAlias.Trim()}.{quotedCol}";
    }

    private static string QuoteIdent(string ident)
    {
        var trimmed = ident.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return trimmed;
        return $"\"{trimmed.Replace("\"", "\"\"")}\"";
    }

    private static string FormatFilterValue(string rawValue)
    {
        var trimmed = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            return "''";

        if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\'')) ||
            double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
            string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"'{trimmed.Replace("'", "''")}'";
    }
}
