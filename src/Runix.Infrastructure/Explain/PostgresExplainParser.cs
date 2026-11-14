using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Runix.Core.Models;

namespace Runix.Infrastructure.Explain;

/// <summary>
/// Parser de planos de execução JSON do PostgreSQL (EXPLAIN ANALYZE BUFFERS FORMAT JSON).
/// </summary>
public static class PostgresExplainParser
{
    public static ExecutionPlanModel Parse(string jsonOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            return new ExecutionPlanModel
            {
                Dialect = "PostgreSQL",
                RawOutput = jsonOutput
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var rootArray = doc.RootElement;
            JsonElement topObject;

            if (rootArray.ValueKind == JsonValueKind.Array && rootArray.GetArrayLength() > 0)
            {
                topObject = rootArray[0];
            }
            else if (rootArray.ValueKind == JsonValueKind.Object)
            {
                topObject = rootArray;
            }
            else
            {
                throw new FormatException("Formato JSON de Explain do PostgreSQL inválido.");
            }

            var model = new ExecutionPlanModel
            {
                Dialect = "PostgreSQL",
                RawOutput = jsonOutput
            };

            if (topObject.TryGetProperty("Planning Time", out var planningElem))
            {
                model.PlanningTimeMs = planningElem.GetDouble();
            }

            if (topObject.TryGetProperty("Execution Time", out var execElem))
            {
                model.ExecutionTimeMs = execElem.GetDouble();
                model.HasAnalyzeData = true;
            }

            if (topObject.TryGetProperty("Plan", out var planElem))
            {
                var rootNode = ParseNode(planElem);
                model.RootNode = rootNode;
                model.TotalCost = rootNode.TotalCost;

                // Compute relative cost percentages and bottlenecks across the entire tree
                ComputeCostPercentages(rootNode, model.TotalCost);
            }

            return model;
        }
        catch (Exception ex)
        {
            return new ExecutionPlanModel
            {
                Dialect = "PostgreSQL",
                RawOutput = $"Erro ao parsear plano PostgreSQL: {ex.Message}\nRaw:\n{jsonOutput}"
            };
        }
    }

    private static ExplainNode ParseNode(JsonElement elem)
    {
        var node = new ExplainNode();

        if (elem.TryGetProperty("Node Type", out var nodeType))
            node.NodeType = nodeType.GetString() ?? "Node";

        if (elem.TryGetProperty("Relation Name", out var relation))
            node.RelationName = relation.GetString();

        if (elem.TryGetProperty("Index Name", out var index))
            node.IndexName = index.GetString();

        if (elem.TryGetProperty("Alias", out var alias))
            node.Alias = alias.GetString();

        // Costs
        if (elem.TryGetProperty("Startup Cost", out var startupCost))
            node.StartupCost = startupCost.GetDouble();

        if (elem.TryGetProperty("Total Cost", out var totalCost))
            node.TotalCost = totalCost.GetDouble();

        if (elem.TryGetProperty("Plan Rows", out var planRows))
            node.PlanRows = planRows.GetInt64();

        if (elem.TryGetProperty("Plan Width", out var planWidth))
            node.PlanWidth = planWidth.GetInt32();

        // Actual timings (ANALYZE)
        if (elem.TryGetProperty("Actual Startup Time", out var actStartup))
            node.ActualStartupTimeMs = actStartup.GetDouble();

        if (elem.TryGetProperty("Actual Total Time", out var actTotal))
            node.ActualTotalTimeMs = actTotal.GetDouble();

        if (elem.TryGetProperty("Actual Rows", out var actRows))
            node.ActualRows = actRows.GetInt64();

        if (elem.TryGetProperty("Actual Loops", out var actLoops))
            node.ActualLoops = actLoops.GetInt64();

        // Filters and conditions
        if (elem.TryGetProperty("Filter", out var filter))
            node.FilterCondition = filter.GetString();

        if (elem.TryGetProperty("Rows Removed by Filter", out var rowsRemoved))
            node.RowsRemovedByFilter = rowsRemoved.GetInt64();

        if (elem.TryGetProperty("Hash Cond", out var hashCond))
            node.HashCondition = hashCond.GetString();

        if (elem.TryGetProperty("Index Cond", out var indexCond))
            node.IndexCondition = indexCond.GetString();

        // Buffers
        if (elem.TryGetProperty("Shared Hit Blocks", out var hit))
            node.SharedHitBlocks = hit.GetInt64();

        if (elem.TryGetProperty("Shared Read Blocks", out var read))
            node.SharedReadBlocks = read.GetInt64();

        if (elem.TryGetProperty("Shared Dirtied Blocks", out var dirtied))
            node.SharedDirtiedBlocks = dirtied.GetInt64();

        if (elem.TryGetProperty("Shared Written Blocks", out var written))
            node.SharedWrittenBlocks = written.GetInt64();

        // Operator category classification
        node.OperatorCategory = ClassifyOperator(node.NodeType);

        // Subnodes (Plans)
        if (elem.TryGetProperty("Plans", out var plansArray) && plansArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var childElem in plansArray.EnumerateArray())
            {
                node.Children.Add(ParseNode(childElem));
            }
        }

        return node;
    }

    private static void ComputeCostPercentages(ExplainNode node, double totalTreeCost)
    {
        if (totalTreeCost > 0)
        {
            // Compute percentage of cost attributed to this node
            node.CostPercent = Math.Clamp((node.TotalCost / totalTreeCost) * 100.0, 0.0, 100.0);
        }
        else
        {
            node.CostPercent = 0.0;
        }

        // Identify bottleneck: high percentage leaf node, or Seq Scan with filter
        var isLeaf = node.Children.Count == 0;
        var isSeqScanWithFilter = node.NodeType.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase) && 
                                  (!string.IsNullOrEmpty(node.FilterCondition) || (node.RowsRemovedByFilter ?? 0) > 0);

        if (isSeqScanWithFilter || (node.CostPercent >= 50.0 && isLeaf))
        {
            node.IsBottleneck = true;
        }

        foreach (var child in node.Children)
        {
            ComputeCostPercentages(child, totalTreeCost);
        }
    }

    public static string ClassifyOperator(string nodeType)
    {
        var lower = nodeType.ToLowerInvariant();
        if (lower.Contains("scan")) return "Scan";
        if (lower.Contains("join")) return "Join";
        if (lower.Contains("sort")) return "Sort";
        if (lower.Contains("aggregate") || lower.Contains("group")) return "Aggregate";
        if (lower.Contains("limit") || lower.Contains("unique")) return "Filter";
        return "Operation";
    }
}
