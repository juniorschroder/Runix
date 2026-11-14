using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de exportação multi-formato de dados (CSV, JSON, Excel, SQL Dump).
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Exporta dados carregados em memória (colunas e lista de linhas).
    /// </summary>
    Task<ExportResult> ExportDataAsync(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Exporta dados diretamente em streaming a partir de um DbDataReader aberto.
    /// </summary>
    Task<ExportResult> ExportFromReaderAsync(
        DbDataReader reader,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
