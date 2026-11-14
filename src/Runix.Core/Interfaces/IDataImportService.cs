using System;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de importação interativa de dados (CSV, JSON, Excel).
/// </summary>
public interface IDataImportService
{
    /// <summary>
    /// Analisa o arquivo fornecido, detectando o formato, delimitador e lendo as primeiras linhas para preview.
    /// </summary>
    Task<ImportPreviewData> PreviewFileAsync(
        string filePath,
        ImportFileFormat? formatHint = null,
        char? delimiterHint = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executa o processo de importação para a tabela de destino usando mapeamentos e estratégias configuradas.
    /// </summary>
    Task<ImportExecutionResult> ExecuteImportAsync(
        IDatabaseService databaseService,
        ImportConfiguration config,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
