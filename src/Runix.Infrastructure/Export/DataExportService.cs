using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Implementação concreta do serviço de exportação multi-formato.
/// </summary>
public class DataExportService : IDataExportService
{
    public static DataExportService Instance { get; } = new();

    public async Task<ExportResult> ExportDataAsync(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            return ExportResult.Failure("Caminho de arquivo de destino não especificado.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(options.FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            long rowsCount;
            await using (var fileStream = new FileStream(
                options.FilePath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                bufferSize: 65536, 
                useAsync: true))
            {
                rowsCount = options.Format switch
                {
                    ExportFormat.Csv => await CsvExporter.ExportAsync(fileStream, columns, rows, options, progress, ct),
                    ExportFormat.Json => await JsonExporter.ExportAsync(fileStream, columns, rows, options, progress, ct),
                    ExportFormat.Excel => await ExcelExporter.ExportAsync(fileStream, columns, rows, options, progress, ct),
                    ExportFormat.SqlDump => await SqlDumpExporter.ExportAsync(fileStream, columns, rows, options, progress, ct),
                    _ => throw new NotSupportedException($"Formato de exportação {options.Format} não suportado.")
                };
            }

            sw.Stop();
            var fileInfo = new FileInfo(options.FilePath);

            return new ExportResult
            {
                Success = true,
                RowsExported = rowsCount,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                FilePath = options.FilePath
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            TryDeleteFile(options.FilePath);
            return ExportResult.Failure("Operação de exportação cancelada pelo usuário.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExportResult.Failure($"Erro ao exportar dados: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<ExportResult> ExportFromReaderAsync(
        DbDataReader reader,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            return ExportResult.Failure("Caminho de arquivo de destino não especificado.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(options.FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            long rowsCount;
            await using (var fileStream = new FileStream(
                options.FilePath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                bufferSize: 65536, 
                useAsync: true))
            {
                rowsCount = options.Format switch
                {
                    ExportFormat.Csv => await CsvExporter.ExportFromReaderAsync(fileStream, reader, options, progress, ct),
                    ExportFormat.Json => await JsonExporter.ExportFromReaderAsync(fileStream, reader, options, progress, ct),
                    ExportFormat.Excel => await ExcelExporter.ExportFromReaderAsync(fileStream, reader, options, progress, ct),
                    ExportFormat.SqlDump => await SqlDumpExporter.ExportFromReaderAsync(fileStream, reader, options, progress, ct),
                    _ => throw new NotSupportedException($"Formato de exportação {options.Format} não suportado.")
                };
            }

            sw.Stop();
            var fileInfo = new FileInfo(options.FilePath);

            return new ExportResult
            {
                Success = true,
                RowsExported = rowsCount,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                FilePath = options.FilePath
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            TryDeleteFile(options.FilePath);
            return ExportResult.Failure("Operação de exportação cancelada pelo usuário.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExportResult.Failure($"Erro ao exportar dados: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }
}
