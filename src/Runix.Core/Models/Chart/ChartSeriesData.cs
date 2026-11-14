using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models.Chart;

/// <summary>
/// Coleção agregada de pontos e estatísticas globais para desenho de eixos e séries.
/// </summary>
public class ChartSeriesData
{
    public string SeriesName { get; set; } = "Série";
    public string XAxisLabel { get; set; } = string.Empty;
    public string YAxisLabel { get; set; } = string.Empty;
    public List<ChartDataPoint> Points { get; set; } = new();

    public double MinValue => Points.Count > 0 ? Points.Min(p => p.Value) : 0;
    public double MaxValue => Points.Count > 0 ? Points.Max(p => p.Value) : 0;
    public double TotalSum => Points.Count > 0 ? Points.Sum(p => p.Value) : 0;
    public int Count => Points.Count;
}
