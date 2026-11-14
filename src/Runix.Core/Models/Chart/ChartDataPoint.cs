namespace Runix.Core.Models.Chart;

/// <summary>
/// Representa uma entrada ou ponto individual em uma série de dados a ser plotada.
/// </summary>
public class ChartDataPoint
{
    public string Category { get; set; } = string.Empty;
    public double Value { get; set; }
    public string FormattedValue { get; set; } = string.Empty;
    public double? Percentage { get; set; }
    public string ColorHex { get; set; } = "#38bdf8";
    public double XSecondary { get; set; }
}
