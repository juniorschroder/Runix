using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Runix.Core.Models;

namespace Runix.Infrastructure.Explain;

/// <summary>
/// Parser para a árvore tabular gerada por EXPLAIN QUERY PLAN no SQLite.
/// </summary>
public static class SqliteExplainParser
{
    private static readonly Regex ScanTableRegex = new(@"\bSCAN\s+(?:TABLE\s+)?([a-zA-Z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SearchIndexRegex = new(@"\bSEARCH\s+(?:TABLE\s+)?([a-zA-Z0-9_]+)\s+USING\s+INDEX\s+([a-zA-Z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SearchPkRegex = new(@"\bSEARCH\s+(?:TABLE\s+)?([a-zA-Z0-9_]+)\s+USING\s+(?:INTEGER\s+)?PRIMARY\s+KEY", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ExecutionPlanModel Parse(QueryResult queryPlanResult)
    {
        var model = new ExecutionPlanModel
        {
            Dialect = "SQLite",
            HasAnalyzeData = false
        };

        if (queryPlanResult == null || !queryPlanResult.Success || queryPlanResult.Rows.Count == 0)
        {
            model.RawOutput = queryPlanResult?.ErrorMessage ?? "Nenhum dado retornado pelo EXPLAIN QUERY PLAN.";
            return model;
        }

        // Determine column indices
        int idCol = 0;
        int parentCol = 1;
        int detailCol = queryPlanResult.Columns.Count - 1;

        for (int i = 0; i < queryPlanResult.Columns.Count; i++)
        {
            var col = queryPlanResult.Columns[i].ToLowerInvariant();
            if (col == "id" || col == "selectid") idCol = i;
            else if (col == "parent") parentCol = i;
            else if (col == "detail") detailCol = i;
        }

        var rawLines = new List<string>();
        var nodesById = new Dictionary<int, ExplainNode>();
        var parentMapping = new Dictionary<int, int>();

        foreach (var row in queryPlanResult.Rows)
        {
            if (row.Count <= detailCol) continue;

            int id = Convert.ToInt32(row[idCol]);
            int parent = Convert.ToInt32(row[parentCol]);
            string detail = row[detailCol]?.ToString() ?? string.Empty;

            rawLines.Add($"[{id} -> {parent}] {detail}");

            var node = CreateNodeFromDetail(id, detail);
            nodesById[id] = node;
            parentMapping[id] = parent;
        }

        model.RawOutput = string.Join("\n", rawLines);

        // Build hierarchical tree
        ExplainNode? root = null;
        var childrenMap = new Dictionary<int, List<ExplainNode>>();

        foreach (var (id, parent) in parentMapping)
        {
            if (parent == 0 || !nodesById.ContainsKey(parent))
            {
                if (root == null)
                {
                    root = nodesById[id];
                }
                else
                {
                    // If multiple top-level nodes, attach under a synthetic root
                    if (root.NodeType != "QUERY PLAN")
                    {
                        var syntheticRoot = new ExplainNode
                        {
                            NodeType = "QUERY PLAN",
                            OperatorCategory = "Operation"
                        };
                        syntheticRoot.Children.Add(root);
                        root = syntheticRoot;
                    }
                    root.Children.Add(nodesById[id]);
                }
            }
            else
            {
                if (!childrenMap.ContainsKey(parent))
                {
                    childrenMap[parent] = new List<ExplainNode>();
                }
                childrenMap[parent].Add(nodesById[id]);
            }
        }

        // Wire children to parents
        foreach (var (parentId, children) in childrenMap)
        {
            if (nodesById.TryGetValue(parentId, out var parentNode))
            {
                parentNode.Children.AddRange(children);
            }
        }

        // Default root if empty
        if (root == null && nodesById.Count > 0)
        {
            root = nodesById.Values.First();
        }

        model.RootNode = root;
        return model;
    }

    private static ExplainNode CreateNodeFromDetail(int id, string detail)
    {
        var node = new ExplainNode
        {
            Id = id.ToString()
        };

        // 1. Search Using Index
        var searchIndexMatch = SearchIndexRegex.Match(detail);
        if (searchIndexMatch.Success)
        {
            node.RelationName = searchIndexMatch.Groups[1].Value;
            node.IndexName = searchIndexMatch.Groups[2].Value;
            node.NodeType = "SEARCH TABLE (Index)";
            node.OperatorCategory = "Index Scan";
            node.CostPercent = 15.0; // Index searches are light
            return node;
        }

        // 2. Search Using Primary Key
        var searchPkMatch = SearchPkRegex.Match(detail);
        if (searchPkMatch.Success)
        {
            node.RelationName = searchPkMatch.Groups[1].Value;
            node.IndexName = "PRIMARY KEY";
            node.NodeType = "SEARCH TABLE (PK)";
            node.OperatorCategory = "Index Scan";
            node.CostPercent = 10.0;
            return node;
        }

        // 3. Scan Table (Unindexed sequential scan -> Bottleneck!)
        var scanMatch = ScanTableRegex.Match(detail);
        if (scanMatch.Success)
        {
            node.RelationName = scanMatch.Groups[1].Value;
            node.NodeType = "SCAN TABLE (Seq Scan)";
            node.OperatorCategory = "Scan";
            node.IsBottleneck = true;
            node.CostPercent = 85.0; // Mark table scan as bottleneck
            return node;
        }

        // 4. Temporary B-Tree
        if (detail.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase))
        {
            node.NodeType = "TEMP B-TREE";
            node.OperatorCategory = "Sort";
            node.CostPercent = 45.0;
            return node;
        }

        // Generic classification
        node.NodeType = detail;
        node.OperatorCategory = "Operation";
        node.CostPercent = 20.0;
        return node;
    }
}
