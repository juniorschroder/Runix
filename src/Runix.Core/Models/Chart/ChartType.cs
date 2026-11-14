namespace Runix.Core.Models.Chart;

/// <summary>
/// Tipologias de gráficos suportados pela engine de renderização SkiaSharp.
/// </summary>
public enum ChartType
{
    /// <summary>
    /// Gráfico de colunas/barras verticais.
    /// </summary>
    BarVertical,

    /// <summary>
    /// Gráfico de barras horizontais.
    /// </summary>
    BarHorizontal,

    /// <summary>
    /// Gráfico de linha contínua com área sombreada translúcida.
    /// </summary>
    LineArea,

    /// <summary>
    /// Gráfico de dispersão cartesiana bidimensional.
    /// </summary>
    Scatter,

    /// <summary>
    /// Gráfico de pizza com recorte central (donut).
    /// </summary>
    Donut
}
