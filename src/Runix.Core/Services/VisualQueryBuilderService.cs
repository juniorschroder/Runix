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
                var colRef = FormatColumnRef(col.TableAlias, col.ColumnName, dialect);
                var expr = FormatAggregateExpression(colRef, col.AggregateFunction);

                if (!string.IsNullOrWhiteSpace(col.OutputAlias))
                {
                    expr += $" AS {QuoteIdentifier(col.OutputAlias, dialect)}";
                }
                selectParts.Add(expr);
            }
            sb.Append(string.Join(", ", selectParts));
        }

        // 2. FROM Clause
        sb.Append($" FROM {QuoteIdentifier(model.BaseTable, dialect)}");
        if (!string.IsNullOrWhiteSpace(model.BaseTableAlias) &&
            !string.Equals(model.BaseTableAlias, model.BaseTable, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" {model.BaseTableAlias.Trim()}");
        }

        // 3. JOIN Clauses
        foreach (var join in model.Joins.Where(j => !string.IsNullOrWhiteSpace(j.TargetTable)))
        {
            var jType = NormalizeJoinType(join.JoinType);
            sb.Append($" {jType} {QuoteIdentifier(join.TargetTable, dialect)}");

            if (!string.IsNullOrWhiteSpace(join.TargetAlias) &&
                !string.Equals(join.TargetAlias, join.TargetTable, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append($" {join.TargetAlias.Trim()}");
            }

            if (jType != "CROSS JOIN")
            {
                var onConditions = new List<string>();

                // Primary condition
                if (!string.IsNullOrWhiteSpace(join.SourceColumn) && !string.IsNullOrWhiteSpace(join.TargetColumn))
                {
                    var leftRef = FormatColumnRef(
                        string.IsNullOrWhiteSpace(join.SourceTableAlias)
                            ? (string.IsNullOrWhiteSpace(model.BaseTableAlias) ? model.BaseTable : model.BaseTableAlias)
                            : join.SourceTableAlias,
                        join.SourceColumn,
                        dialect);

                    var rightRef = FormatColumnRef(
                        string.IsNullOrWhiteSpace(join.TargetAlias) ? join.TargetTable : join.TargetAlias,
                        join.TargetColumn,
                        dialect);

                    var op = string.IsNullOrWhiteSpace(join.Operator) ? "=" : join.Operator.Trim();
                    onConditions.Add($"{leftRef} {op} {rightRef}");
                }

                // Additional multi-column conditions
                if (join.AdditionalConditions != null)
                {
                    foreach (var cond in join.AdditionalConditions.Where(c => !string.IsNullOrWhiteSpace(c.SourceColumn) && !string.IsNullOrWhiteSpace(c.TargetColumn)))
                    {
                        var leftRef = FormatColumnRef(
                            string.IsNullOrWhiteSpace(join.SourceTableAlias)
                                ? (string.IsNullOrWhiteSpace(model.BaseTableAlias) ? model.BaseTable : model.BaseTableAlias)
                                : join.SourceTableAlias,
                            cond.SourceColumn,
                            dialect);

                        var rightRef = FormatColumnRef(
                            string.IsNullOrWhiteSpace(join.TargetAlias) ? join.TargetTable : join.TargetAlias,
                            cond.TargetColumn,
                            dialect);

                        var op = string.IsNullOrWhiteSpace(cond.Operator) ? "=" : cond.Operator.Trim();
                        onConditions.Add($"{leftRef} {op} {rightRef}");
                    }
                }

                if (onConditions.Count > 0)
                {
                    sb.Append(" ON ").Append(string.Join(" AND ", onConditions));
                }
            }
        }

        // 4. WHERE Clause
        string whereSql = string.Empty;
        if (model.FilterTree != null && !model.FilterTree.IsEmpty)
        {
            whereSql = FormatFilterNode(model.FilterTree, dialect, isRoot: true);
        }
        else if (model.Filters != null && model.Filters.Count > 0)
        {
            whereSql = FormatFlatFilters(model.Filters, dialect);
        }

        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            sb.Append(" WHERE ").Append(whereSql);
        }

        // 5. GROUP BY Clause
        var groupByParts = new List<string>();

        // Explicit GroupByColumns
        if (model.GroupByColumns != null && model.GroupByColumns.Count > 0)
        {
            foreach (var col in model.GroupByColumns.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var (alias, colName) = SplitQualified(col);
                groupByParts.Add(FormatColumnRef(alias, colName, dialect));
            }
        }
        else
        {
            // Explicit IsGroupBy on SelectColumns
            var explicitGroups = selectedCols.Where(c => c.IsGroupBy).ToList();
            if (explicitGroups.Count > 0)
            {
                groupByParts.AddRange(explicitGroups.Select(c => FormatColumnRef(c.TableAlias, c.ColumnName, dialect)));
            }
            else
            {
                // Auto-group by non-aggregated columns if any aggregate function is present
                bool hasAggregate = selectedCols.Any(c => !string.IsNullOrWhiteSpace(c.AggregateFunction) &&
                                                          !c.AggregateFunction.Equals("NONE", StringComparison.OrdinalIgnoreCase));
                if (hasAggregate)
                {
                    var nonAggregated = selectedCols.Where(c => string.IsNullOrWhiteSpace(c.AggregateFunction) ||
                                                                c.AggregateFunction.Equals("NONE", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (nonAggregated.Count > 0)
                    {
                        groupByParts.AddRange(nonAggregated.Select(c => FormatColumnRef(c.TableAlias, c.ColumnName, dialect)));
                    }
                }
            }
        }

        if (groupByParts.Count > 0)
        {
            sb.Append(" GROUP BY ").Append(string.Join(", ", groupByParts.Distinct()));
        }

        // 6. HAVING Clause
        if (model.HavingTree != null && !model.HavingTree.IsEmpty)
        {
            var havingSql = FormatFilterNode(model.HavingTree, dialect, isRoot: true);
            if (!string.IsNullOrWhiteSpace(havingSql))
            {
                sb.Append(" HAVING ").Append(havingSql);
            }
        }

        // 7. ORDER BY Clause
        var validSorts = model.Sorts?.Where(s => !string.IsNullOrWhiteSpace(s.ColumnName)).ToList() ?? new List<VisualSortClause>();
        bool hasExplicitOrderBy = validSorts.Count > 0;

        if (hasExplicitOrderBy)
        {
            var sortParts = validSorts.Select(s =>
            {
                var dir = string.Equals(s.Direction, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                return $"{FormatColumnRef(s.TableAlias, s.ColumnName, dialect)} {dir}";
            });
            sb.Append(" ORDER BY ").Append(string.Join(", ", sortParts));
        }

        // 8. LIMIT & OFFSET / PAGINATION
        FormatPagination(sb, model, dialect, hasExplicitOrderBy);

        sb.Append(';');
        return _formatter.Format(sb.ToString());
    }

    private static void FormatPagination(StringBuilder sb, VisualQueryModel model, DatabaseType dialect, bool hasExplicitOrderBy)
    {
        if (dialect == DatabaseType.SqlServer)
        {
            if (model.Limit.HasValue || model.Offset.HasValue)
            {
                if (!hasExplicitOrderBy)
                {
                    sb.Append(" ORDER BY (SELECT NULL)");
                }

                int offset = model.Offset ?? 0;
                int limit = model.Limit ?? 100;
                sb.Append($" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
            }
        }
        else if (dialect == DatabaseType.Oracle)
        {
            if (model.Limit.HasValue || model.Offset.HasValue)
            {
                if (!hasExplicitOrderBy)
                {
                    sb.Append(" ORDER BY 1");
                }

                int offset = model.Offset ?? 0;
                int limit = model.Limit ?? 100;
                sb.Append($" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
            }
        }
        else
        {
            if (model.Limit.HasValue && model.Limit.Value > 0)
            {
                sb.Append($" LIMIT {model.Limit.Value}");
            }

            if (model.Offset.HasValue && model.Offset.Value > 0)
            {
                sb.Append($" OFFSET {model.Offset.Value}");
            }
        }
    }

    private static string FormatAggregateExpression(string colRef, string? aggregateFunction)
    {
        if (string.IsNullOrWhiteSpace(aggregateFunction) || aggregateFunction.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return colRef;

        var upper = aggregateFunction.Trim().ToUpperInvariant();
        if (upper == "COUNT_DISTINCT" || upper == "COUNT(DISTINCT)")
        {
            return $"COUNT(DISTINCT {colRef})";
        }

        return $"{upper}({colRef})";
    }

    public static string FormatFilterNode(VisualFilterNode node, DatabaseType dialect, bool isRoot = false)
    {
        if (node is VisualFilterCondition cond)
        {
            return FormatCondition(cond, dialect);
        }

        if (node is VisualFilterGroup group)
        {
            var parts = new List<string>();
            foreach (var child in group.Children)
            {
                var childSql = FormatFilterNode(child, dialect, isRoot: false);
                if (!string.IsNullOrWhiteSpace(childSql))
                {
                    parts.Add(childSql);
                }
            }

            if (parts.Count == 0) return string.Empty;

            var op = group.LogicalOperator.Equals("OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";
            var combined = string.Join($" {op} ", parts);

            if (isRoot && !group.IsNegated)
            {
                return combined;
            }

            return group.IsNegated ? $"NOT ({combined})" : $"({combined})";
        }

        return string.Empty;
    }

    private static string FormatCondition(VisualFilterCondition f, DatabaseType dialect)
    {
        if (string.IsNullOrWhiteSpace(f.ColumnName)) return string.Empty;

        var colRef = FormatColumnRef(f.TableAlias, f.ColumnName, dialect);
        var op = string.IsNullOrWhiteSpace(f.ComparisonOperator) ? "=" : f.ComparisonOperator.Trim().ToUpperInvariant();

        switch (op)
        {
            case "IS NULL":
            case "IS NOT NULL":
                return $"{colRef} {op}";

            case "ILIKE":
                if (dialect == DatabaseType.PostgreSQL)
                {
                    return $"{colRef} ILIKE {FormatFilterValue(f.Value)}";
                }
                // Dialects without native ILIKE fallback to LIKE
                return $"{colRef} LIKE {FormatFilterValue(f.Value)}";

            case "LIKE":
            case "NOT LIKE":
                return $"{colRef} {op} {FormatFilterValue(f.Value)}";

            case "CONTAINS":
                return $"{colRef} LIKE {FormatLikePattern(f.Value, "%", "%")}";

            case "STARTS WITH":
                return $"{colRef} LIKE {FormatLikePattern(f.Value, "", "%")}";

            case "ENDS WITH":
                return $"{colRef} LIKE {FormatLikePattern(f.Value, "%", "")}";

            case "IN":
            case "NOT IN":
            {
                var rawVal = f.Value?.Trim() ?? string.Empty;
                if (!rawVal.StartsWith('('))
                    rawVal = $"({rawVal})";
                return $"{colRef} {op} {rawVal}";
            }

            case "BETWEEN":
            case "NOT BETWEEN":
            {
                var val1 = FormatFilterValue(f.Value);
                var val2 = FormatFilterValue(f.SecondValue);
                return $"{colRef} {op} {val1} AND {val2}";
            }

            default:
                return $"{colRef} {op} {FormatFilterValue(f.Value)}";
        }
    }

    private static string FormatFlatFilters(List<VisualFilterCondition> filters, DatabaseType dialect)
    {
        var validFilters = filters.Where(f => !string.IsNullOrWhiteSpace(f.ColumnName)).ToList();
        if (validFilters.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < validFilters.Count; i++)
        {
            var f = validFilters[i];
            if (i > 0)
            {
                var logic = string.Equals(f.LogicalOperator, "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";
                sb.Append($" {logic} ");
            }

            sb.Append(FormatCondition(f, dialect));
        }

        return sb.ToString();
    }

    public VisualJoinClause SuggestJoinClause(
        string baseTable,
        string baseAlias,
        string joinTable,
        string joinAlias,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var options = SuggestJoinOptions(baseTable, baseAlias, joinTable, joinAlias, foreignKeys);
        return options.Count > 0 ? options[0] : CreateDefaultJoin(baseTable, baseAlias, joinTable, joinAlias);
    }

    public IReadOnlyList<VisualJoinClause> SuggestJoinOptions(
        string baseTable,
        string baseAlias,
        string joinTable,
        string joinAlias,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var effectiveBaseAlias = string.IsNullOrWhiteSpace(baseAlias) ? baseTable : baseAlias;
        var effectiveJoinAlias = string.IsNullOrWhiteSpace(joinAlias) ? joinTable : joinAlias;
        var results = new List<VisualJoinClause>();

        if (foreignKeys != null)
        {
            // Case 1: Forward FK: baseTable -> joinTable
            var forwardFks = foreignKeys.Where(fk =>
                string.Equals(fk.SourceTable, baseTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.TargetTable, joinTable, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var fk in forwardFks)
            {
                results.Add(new VisualJoinClause
                {
                    JoinType = "INNER JOIN",
                    TargetTable = joinTable,
                    TargetAlias = joinAlias,
                    SourceTableAlias = effectiveBaseAlias,
                    SourceColumn = fk.SourceColumn,
                    Operator = "=",
                    TargetColumn = fk.TargetColumn,
                    RelationshipDescription = $"FK Direta: {baseTable}.{fk.SourceColumn} → {joinTable}.{fk.TargetColumn}"
                });
            }

            // Case 2: Reverse FK: joinTable -> baseTable
            var reverseFks = foreignKeys.Where(fk =>
                string.Equals(fk.SourceTable, joinTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.TargetTable, baseTable, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var fk in reverseFks)
            {
                results.Add(new VisualJoinClause
                {
                    JoinType = "INNER JOIN",
                    TargetTable = joinTable,
                    TargetAlias = joinAlias,
                    SourceTableAlias = effectiveBaseAlias,
                    SourceColumn = fk.TargetColumn,
                    Operator = "=",
                    TargetColumn = fk.SourceColumn,
                    RelationshipDescription = $"FK Reversa: {joinTable}.{fk.SourceColumn} → {baseTable}.{fk.TargetColumn}"
                });
            }
        }

        if (results.Count == 0)
        {
            results.Add(CreateDefaultJoin(baseTable, baseAlias, joinTable, joinAlias));
        }

        return results;
    }

    private static VisualJoinClause CreateDefaultJoin(string baseTable, string baseAlias, string joinTable, string joinAlias)
    {
        var effectiveBaseAlias = string.IsNullOrWhiteSpace(baseAlias) ? baseTable : baseAlias;
        var singularBase = baseTable.EndsWith("s", StringComparison.OrdinalIgnoreCase) && baseTable.Length > 1
            ? baseTable[..^1]
            : baseTable;

        return new VisualJoinClause
        {
            JoinType = "INNER JOIN",
            TargetTable = joinTable,
            TargetAlias = joinAlias,
            SourceTableAlias = effectiveBaseAlias,
            SourceColumn = "id",
            Operator = "=",
            TargetColumn = $"{singularBase}_id",
            RelationshipDescription = $"Convenção: {baseTable}.id = {joinTable}.{singularBase}_id"
        };
    }

    private static string NormalizeJoinType(string? joinType)
    {
        if (string.IsNullOrWhiteSpace(joinType)) return "INNER JOIN";

        var upper = joinType.Trim().ToUpperInvariant();
        if (upper.Equals("CROSS", StringComparison.OrdinalIgnoreCase) || upper.Equals("CROSS JOIN", StringComparison.OrdinalIgnoreCase))
            return "CROSS JOIN";

        if (upper.Equals("FULL", StringComparison.OrdinalIgnoreCase) || upper.Equals("FULL JOIN", StringComparison.OrdinalIgnoreCase) || upper.Equals("FULL OUTER", StringComparison.OrdinalIgnoreCase) || upper.Equals("FULL OUTER JOIN", StringComparison.OrdinalIgnoreCase))
            return "FULL OUTER JOIN";

        if (!upper.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase))
            upper += " JOIN";

        return upper;
    }

    public static string FormatColumnRef(string? tableAlias, string columnName, DatabaseType dialect = DatabaseType.SQLite)
    {
        if (columnName == "*")
            return string.IsNullOrWhiteSpace(tableAlias) ? "*" : $"{tableAlias.Trim()}.*";

        var quotedCol = QuoteIdentifier(columnName, dialect);
        return string.IsNullOrWhiteSpace(tableAlias)
            ? quotedCol
            : $"{tableAlias.Trim()}.{quotedCol}";
    }

    public static string QuoteIdentifier(string identifier, DatabaseType dialect)
    {
        var trimmed = identifier.Trim();

        return dialect switch
        {
            DatabaseType.SqlServer =>
                (trimmed.StartsWith('[') && trimmed.EndsWith(']')) ? trimmed : $"[{trimmed.Replace("]", "]]")}]",

            DatabaseType.MySQL or DatabaseType.MariaDB =>
                (trimmed.StartsWith('`') && trimmed.EndsWith('`')) ? trimmed : $"`{trimmed.Replace("`", "``")}`",

            DatabaseType.Oracle =>
                (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
                    ? trimmed.ToUpperInvariant()
                    : $"\"{trimmed.Replace("\"", "\"\"").ToUpperInvariant()}\"",

            _ =>
                (trimmed.StartsWith('"') && trimmed.EndsWith('"')) ? trimmed : $"\"{trimmed.Replace("\"", "\"\"")}\""
        };
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

    private static string FormatLikePattern(string rawValue, string prefix, string suffix)
    {
        var trimmed = rawValue?.Trim() ?? string.Empty;
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1];
        }

        var escaped = trimmed.Replace("'", "''");
        return $"'{prefix}{escaped}{suffix}'";
    }

    private static (string Alias, string Column) SplitQualified(string entry)
    {
        var dot = entry.IndexOf('.');
        if (dot > 0 && dot < entry.Length - 1)
        {
            return (entry[..dot], entry[(dot + 1)..]);
        }
        return (string.Empty, entry);
    }
}
