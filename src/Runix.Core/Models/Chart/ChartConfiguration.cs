namespace Runix.Core.Models.Chart;

/// <summary>
/// Configuração de mapeamento, tipologia e limites de renderização de um gráfico.
/// </summary>
public class ChartConfiguration
{
    public ChartType ChartType { get; set; } = ChartType.BarVertical;
    public string XColumn { get; set; } = string.Empty;
    public string YColumn { get; set; } = string.Empty;
    public AggregationType Aggregation { get; set; } = AggregationType.None;
    public int? TopN { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ShowLegend { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;
}
