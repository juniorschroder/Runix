using System;
using System.Linq;

namespace Runix.Core.Models;

/// <summary>
/// Formatos suportados para exportação do diagrama de entidade-relacionamento (DER).
/// </summary>
public enum ErdExportFormat
{
    /// <summary>
    /// Documento PDF vetorial nativo de alta fidelidade com texto pesquisável/selecionável.
    /// </summary>
    Pdf,

    /// <summary>
    /// Imagem vetorial escalável SVG (XML limpo para web, Figma e documentação).
    /// </summary>
    Svg,

    /// <summary>
    /// Imagem rasterizada PNG.
    /// </summary>
    Png,

    /// <summary>
    /// Script SQL DDL de criação de schema.
    /// </summary>
    Sql
}

/// <summary>
/// Tema visual de renderização da exportação do DER.
/// </summary>
public enum ErdExportTheme
{
    /// <summary>
    /// Tema escuro RUNIX padrão da aplicação (fundo #090d14, cartões #141d2b, realces ciano/azul).
    /// </summary>
    DarkRunix,

    /// <summary>
    /// Tema claro de alto contraste para impressão e economia de tinta (fundo #ffffff, cartões #f8fafc).
    /// </summary>
    LightPrint
}

/// <summary>
/// Dimensão de página para exportação PDF vetorial.
/// </summary>
public enum ErdPdfPageSize
{
    /// <summary>
    /// Página única com dimensões exatas da caixa delimitadora do diagrama (zoom infinito digital).
    /// </summary>
    FitToDiagram,

    /// <summary>
    /// Folha A4 Paisagem padrão (842 x 595 pt) com escala proporcional uniforme.
    /// </summary>
    A4Landscape,

    /// <summary>
    /// Folha A3 Paisagem padrão (1191 x 842 pt) para esquemas relacionais densos.
    /// </summary>
    A3Landscape
}

/// <summary>
/// Parâmetros de configuração para a exportação de diagramas DER.
/// </summary>
public class ErdExportOptions
{
    public ErdExportFormat Format { get; set; } = ErdExportFormat.Pdf;
    public ErdExportTheme Theme { get; set; } = ErdExportTheme.DarkRunix;
    public ErdPdfPageSize PageSize { get; set; } = ErdPdfPageSize.FitToDiagram;
    public double Padding { get; set; } = 32.0;
    public bool IncludeSubjectAreas { get; set; } = true;
    public bool IncludeCardinalityBadges { get; set; } = true;
}

/// <summary>
/// Caixa delimitadora (Bounding Box) do diagrama DER com margem aplicada.
/// </summary>
public record DiagramBoundingBox(double MinX, double MinY, double MaxX, double MaxY, double Width, double Height)
{
    /// <summary>
    /// Calcula a bounding box exata envolvendo todos os nós e Subject Areas com margem de segurança.
    /// </summary>
    public static DiagramBoundingBox Calculate(ErdGraphModel graph, double padding = 32.0)
    {
        if (graph == null || graph.Nodes.Count == 0)
        {
            return new DiagramBoundingBox(0, 0, 800, 600, 800, 600);
        }

        double minX = graph.Nodes.Min(n => n.X);
        double minY = graph.Nodes.Min(n => n.Y);
        double maxX = graph.Nodes.Max(n => n.X + (n.Width > 0 ? n.Width : 250));
        double maxY = graph.Nodes.Max(n => n.Y + (n.Height > 0 ? n.Height : 200));

        if (graph.SubjectAreas != null && graph.SubjectAreas.Count > 0)
        {
            minX = Math.Min(minX, graph.SubjectAreas.Min(a => a.X));
            minY = Math.Min(minY, graph.SubjectAreas.Min(a => a.Y));
            maxX = Math.Max(maxX, graph.SubjectAreas.Max(a => a.X + a.Width));
            maxY = Math.Max(maxY, graph.SubjectAreas.Max(a => a.Y + a.Height));
        }

        double boundedMinX = minX - padding;
        double boundedMinY = minY - padding;
        double boundedMaxX = maxX + padding;
        double boundedMaxY = maxY + padding;

        return new DiagramBoundingBox(
            boundedMinX,
            boundedMinY,
            boundedMaxX,
            boundedMaxY,
            Math.Max(100, boundedMaxX - boundedMinX),
            Math.Max(100, boundedMaxY - boundedMinY)
        );
    }
}
