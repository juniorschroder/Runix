using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Dados preliminares extraídos na análise prévia do arquivo a ser importado.
/// </summary>
public class ImportPreviewData
{
    public ImportFileFormat DetectedFormat { get; set; } = ImportFileFormat.Csv;
    public char? DetectedDelimiter { get; set; }
    public string DetectedEncoding { get; set; } = "UTF-8";
    public IReadOnlyList<string> Headers { get; set; } = new List<string>();
    public IReadOnlyList<IReadOnlyList<object?>> PreviewRows { get; set; } = new List<IReadOnlyList<object?>>();
    public long EstimatedTotalRows { get; set; }
}
