using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Representa um nó hierárquico na árvore de plano de execução de uma consulta SQL.
/// </summary>
public class ExplainNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string NodeType { get; set; } = string.Empty;
    public string OperatorCategory { get; set; } = "Scan";
    public string? RelationName { get; set; }
    public string? IndexName { get; set; }
    public string? Alias { get; set; }

    // Estimativas de Custo do Otimizador
    public double StartupCost { get; set; }
    public double TotalCost { get; set; }
    public double CostPercent { get; set; }
    public long PlanRows { get; set; }
    public int PlanWidth { get; set; }

    // Métricas Reais de Execução (Analyze)
    public double? ActualStartupTimeMs { get; set; }
    public double? ActualTotalTimeMs { get; set; }
    public long? ActualRows { get; set; }
    public long? ActualLoops { get; set; }

    // Condições e Filtros
    public string? FilterCondition { get; set; }
    public long? RowsRemovedByFilter { get; set; }
    public string? HashCondition { get; set; }
    public string? IndexCondition { get; set; }

    // Buffers e I/O
    public long? SharedHitBlocks { get; set; }
    public long? SharedReadBlocks { get; set; }
    public long? SharedDirtiedBlocks { get; set; }
    public long? SharedWrittenBlocks { get; set; }

    // Diagnóstico
    public bool IsBottleneck { get; set; }
    public List<ExplainNode> Children { get; set; } = new();

    // Helpers de Formatação e UI
    public string CostColor => CostPercent switch
    {
        >= 50.0 => "#f87171", // Vermelho - Gargalo Crítico
        >= 20.0 => "#fbbf24", // Âmbar - Custo Moderado
        _ => "#34d399"        // Verde - Custo Baixo / Otimizado
    };

    public string CostBadgeText => $"{CostPercent:F1}%";

    public string FormattedActualTime => ActualTotalTimeMs.HasValue
        ? $"{ActualTotalTimeMs.Value:F3} ms"
        : "-";

    public string FormattedRows => ActualRows.HasValue
        ? $"{ActualRows.Value:N0} linhas (est: {PlanRows:N0})"
        : $"{PlanRows:N0} linhas est.";

    public string DisplayTitle => !string.IsNullOrEmpty(RelationName)
        ? $"{NodeType} ({RelationName})"
        : NodeType;

    public string TargetDescription => !string.IsNullOrEmpty(IndexName)
        ? $"Index: {IndexName}"
        : (!string.IsNullOrEmpty(RelationName) ? $"Tabela: {RelationName}" : "");
}
