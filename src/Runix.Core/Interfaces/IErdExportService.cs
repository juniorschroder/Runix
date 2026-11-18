using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de exportação de diagramas ERD em formatos vetoriais e estruturais.
/// </summary>
public interface IErdExportService
{
    /// <summary>
    /// Gera o conteúdo vetorial SVG limpo do diagrama.
    /// </summary>
    Task<string> ExportSvgStringAsync(ErdGraphModel graph, ErdExportOptions options);

    /// <summary>
    /// Gera os bytes do arquivo SVG codificado em UTF-8.
    /// </summary>
    Task<byte[]> ExportSvgAsync(ErdGraphModel graph, ErdExportOptions options);

    /// <summary>
    /// Gera os bytes do documento PDF vetorial nativo via SkiaSharp.
    /// </summary>
    Task<byte[]> ExportPdfAsync(ErdGraphModel graph, ErdExportOptions options);

    /// <summary>
    /// Exporta o diagrama diretamente para um arquivo em disco no formato especificado em options.
    /// </summary>
    Task ExportToFileAsync(string filePath, ErdGraphModel graph, ErdExportOptions options);
}
