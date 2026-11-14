using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Runix.Core.Models;

namespace Runix.Infrastructure.Explain;

/// <summary>
/// Motor heurístico para análise de gargalos e geração de recomendações inteligentes de índices.
/// </summary>
public static class IndexRecommendationEngine
{
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IS", "NULL", "TRUE", "FALSE", "LIKE", "IN", "BETWEEN",
        "SELECT", "FROM", "WHERE", "JOIN", "ON", "ORDER", "BY", "GROUP", "HAVING",
        "LIMIT", "OFFSET", "ASC", "DESC", "CASE", "WHEN", "THEN", "ELSE", "END"
    };

    private static readonly Regex ColumnComparisonRegex = new(
        @"\b([a-zA-Z_][a-zA-Z0-9_]*)\s*(?:<=|>=|!=|<>|=|<|>|\bLIKE\b|\bILIKE\b|\bIN\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<IndexRecommendation> AnalyzePlan(ExecutionPlanModel plan, string originalSql)
    {
        var recommendations = new List<IndexRecommendation>();
        if (plan?.RootNode == null) return recommendations;

        var allNodes = ExecutionPlanModel.FlattenNodes(plan.RootNode).ToList();
        var bottleneckNodes = allNodes.Where(n => 
            n.IsBottleneck || 
            n.NodeType.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase) ||
            n.NodeType.Contains("SCAN TABLE", StringComparison.OrdinalIgnoreCase)).ToList();

        var existingIndexNames = allNodes
            .Where(n => !string.IsNullOrEmpty(n.IndexName))
            .Select(n => n.IndexName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in bottleneckNodes)
        {
            var tableName = node.RelationName;
            if (string.IsNullOrWhiteSpace(tableName)) continue;

            // Extract candidate columns from FilterCondition or SQL
            var candidateCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(node.FilterCondition))
            {
                ExtractColumnsFromExpression(node.FilterCondition, candidateCols);
            }

            if (candidateCols.Count == 0 && !string.IsNullOrEmpty(originalSql))
            {
                ExtractColumnsFromSql(originalSql, tableName, candidateCols);
            }

            if (candidateCols.Count > 0)
            {
                var colsList = candidateCols.Take(3).ToList();
                var indexName = $"idx_{tableName}_{string.Join("_", colsList)}".ToLowerInvariant();

                if (existingIndexNames.Contains(indexName)) continue;

                var isPostgres = plan.Dialect.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);
                var createSql = isPostgres
                    ? $"CREATE INDEX {indexName} ON {tableName} ({string.Join(", ", colsList)});"
                    : $"CREATE INDEX {indexName} ON {tableName} ({string.Join(", ", colsList)});";

                var reason = $"A tabela '{tableName}' sofreu varredura completa (Seq Scan) com filtro em '{string.Join(", ", colsList)}'. " +
                             "A criação deste índice evitará o escaneamento sequencial e reduzirá drasticamente o tempo de resposta.";

                recommendations.Add(new IndexRecommendation
                {
                    TableName = tableName,
                    Columns = colsList,
                    SuggestedIndexName = indexName,
                    CreateIndexSql = createSql,
                    Reason = reason,
                    EstimatedImpactScore = node.CostPercent >= 50.0 ? 95.0 : 75.0
                });
            }
        }

        return recommendations;
    }

    private static void ExtractColumnsFromExpression(string expr, HashSet<string> columns)
    {
        var matches = ColumnComparisonRegex.Matches(expr);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                var col = match.Groups[1].Value.Trim('(', ')', '"', ' ');
                if (!ReservedKeywords.Contains(col) && !double.TryParse(col, out _))
                {
                    columns.Add(col);
                }
            }
        }
    }

    private static void ExtractColumnsFromSql(string sql, string tableName, HashSet<string> columns)
    {
        // Look into WHERE and ON clauses
        var whereIdx = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIdx >= 0)
        {
            var whereClause = sql.Substring(whereIdx);
            var endIdx = whereClause.IndexOfAny(new[] { ';', '\n', '\r' });
            if (endIdx > 0) whereClause = whereClause.Substring(0, endIdx);

            ExtractColumnsFromExpression(whereClause, columns);
        }
    }
}
