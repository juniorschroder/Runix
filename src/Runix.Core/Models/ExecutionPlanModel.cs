using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models;

/// <summary>
/// Modelo consolidado do plano de execução de uma consulta SQL.
/// </summary>
public class ExecutionPlanModel
{
    public string Dialect { get; set; } = "PostgreSQL";
    public bool HasAnalyzeData { get; set; }
    public double PlanningTimeMs { get; set; }
    public double ExecutionTimeMs { get; set; }
    public double TotalCost { get; set; }
    public ExplainNode? RootNode { get; set; }
    public List<IndexRecommendation> Recommendations { get; set; } = new();
    public string RawOutput { get; set; } = string.Empty;

    public bool HasBottlenecks => FlattenNodes(RootNode).Any(n => n.IsBottleneck);
    public int TotalNodeCount => FlattenNodes(RootNode).Count();

    public string FormattedExecutionTime => ExecutionTimeMs > 0
        ? $"{ExecutionTimeMs:F2} ms"
        : (PlanningTimeMs > 0 ? $"{PlanningTimeMs:F2} ms (planejamento)" : "Estimado");

    public static IEnumerable<ExplainNode> FlattenNodes(ExplainNode? node)
    {
        if (node == null) yield break;
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var sub in FlattenNodes(child))
            {
                yield return sub;
            }
        }
    }
}
