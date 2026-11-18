using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Serviço de exportação de diagramas ERD para SVG, PDF e arquivos em disco.
/// </summary>
public class ErdExportService : IErdExportService
{
    public Task<string> ExportSvgStringAsync(ErdGraphModel graph, ErdExportOptions options)
    {
        var svg = SvgErdExporter.ExportSvg(graph, options);
        return Task.FromResult(svg);
    }

    public Task<byte[]> ExportSvgAsync(ErdGraphModel graph, ErdExportOptions options)
    {
        var svg = SvgErdExporter.ExportSvg(graph, options);
        return Task.FromResult(Encoding.UTF8.GetBytes(svg));
    }

    public Task<byte[]> ExportPdfAsync(ErdGraphModel graph, ErdExportOptions options)
    {
        var pdf = SkiaPdfErdExporter.ExportPdf(graph, options);
        return Task.FromResult(pdf);
    }

    public async Task ExportToFileAsync(string filePath, ErdGraphModel graph, ErdExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Caminho do arquivo não pode ser vazio.", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".svg" || options.Format == ErdExportFormat.Svg)
        {
            var bytes = await ExportSvgAsync(graph, options);
            await File.WriteAllBytesAsync(filePath, bytes);
        }
        else if (ext == ".pdf" || options.Format == ErdExportFormat.Pdf)
        {
            var bytes = await ExportPdfAsync(graph, options);
            await File.WriteAllBytesAsync(filePath, bytes);
        }
        else
        {
            // Default to PDF
            var bytes = await ExportPdfAsync(graph, options);
            await File.WriteAllBytesAsync(filePath, bytes);
        }
    }
}
