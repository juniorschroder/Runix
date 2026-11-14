using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Configuração completa de parâmetros para a execução do processo de importação.
/// </summary>
public class ImportConfiguration
{
    public string FilePath { get; set; } = string.Empty;
    public ImportFileFormat Format { get; set; } = ImportFileFormat.Csv;
    public string TargetTable { get; set; } = string.Empty;
    public List<ColumnMapping> Mappings { get; set; } = new();
    public ImportErrorStrategy ErrorStrategy { get; set; } = ImportErrorStrategy.RollbackOnError;
    public int BatchSize { get; set; } = 200;
    public char? Delimiter { get; set; }
    public bool FirstRowIsHeader { get; set; } = true;
}
