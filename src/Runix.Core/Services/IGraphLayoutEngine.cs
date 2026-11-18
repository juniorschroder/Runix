using Runix.Core.Models;

namespace Runix.Core.Services;

/// <summary>
/// Direção do fluxo hierárquico no auto-layout de grafos.
/// </summary>
public enum LayoutDirection
{
    /// <summary>
    /// Fluxo vertical: tabelas independentes no topo, tabelas dependentes abaixo.
    /// </summary>
    TopToBottom,

    /// <summary>
    /// Fluxo horizontal: tabelas independentes à esquerda, dependentes à direita.
    /// </summary>
    LeftToRight
}

/// <summary>
/// Opções de configuração para o motor de auto-layout hierárquico.
/// </summary>
public class GraphLayoutOptions
{
    /// <summary>
    /// Direção principal do fluxo hierárquico.
    /// </summary>
    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;

    /// <summary>
    /// Espaçamento horizontal mínimo entre nós adjacentes (px).
    /// </summary>
    public double NodeSpacingX { get; set; } = 80;

    /// <summary>
    /// Espaçamento vertical mínimo entre nós adjacentes (px).
    /// </summary>
    public double NodeSpacingY { get; set; } = 48;

    /// <summary>
    /// Margem inicial X no canvas (px).
    /// </summary>
    public double MarginX { get; set; } = 40;

    /// <summary>
    /// Margem inicial Y no canvas (px).
    /// </summary>
    public double MarginY { get; set; } = 40;

    /// <summary>
    /// Se verdadeiro, preserva o agrupamento das Subject Areas, organizando seus nós
    /// internamente e empacotando os domínios sem dispersão.
    /// </summary>
    public bool RespectSubjectAreas { get; set; } = true;

    /// <summary>
    /// Espaçamento interno (padding) entre as bordas da Subject Area e suas tabelas internas.
    /// </summary>
    public double SubjectAreaPadding { get; set; } = 28;

    /// <summary>
    /// Espaçamento entre blocos de domínios (Subject Areas) distintos no canvas.
    /// </summary>
    public double DomainSpacing { get; set; } = 80;
}

/// <summary>
/// Contrato do motor de auto-layout para diagramas de entidade-relacionamento (DER).
/// </summary>
public interface IGraphLayoutEngine
{
    /// <summary>
    /// Organiza os nós e domínios visuais do grafo informando novas coordenadas geométricas.
    /// </summary>
    /// <param name="graph">Grafo de entidades e relacionamentos a ser organizado.</param>
    /// <param name="options">Opções de parametrização do layout (direção, espaçamento, domínios).</param>
    void ArrangeLayout(ErdGraphModel graph, GraphLayoutOptions? options = null);
}
