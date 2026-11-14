namespace Runix.Core.Models.Chart;

/// <summary>
/// Funções de agregação estatística em memória para a métrica do Eixo Y.
/// </summary>
public enum AggregationType
{
    /// <summary>
    /// Valores brutos na ordem natural do resultado da query.
    /// </summary>
    None,

    /// <summary>
    /// Soma dos valores numéricos agrupados por categoria.
    /// </summary>
    Sum,

    /// <summary>
    /// Média aritmética dos valores agrupados.
    /// </summary>
    Average,

    /// <summary>
    /// Contagem da frequência de ocorrências na categoria.
    /// </summary>
    Count,

    /// <summary>
    /// Menor valor numérico observado.
    /// </summary>
    Min,

    /// <summary>
    /// Maior valor numérico observado.
    /// </summary>
    Max
}
