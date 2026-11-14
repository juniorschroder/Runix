using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Import;

/// <summary>
/// Serviço de importação de dados multi-formato (CSV, JSON, Excel) com execução transacional resiliente.
/// </summary>
public class DataImportService : IDataImportService
{
    public static DataImportService Instance { get; } = new();

    public async Task<ImportPreviewData> PreviewFileAsync(
        string filePath,
        ImportFileFormat? formatHint = null,
        char? delimiterHint = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arquivo de importação não encontrado.", filePath);
        }

        var format = formatHint ?? DetectFormatFromExtension(filePath);

        return format switch
        {
            ImportFileFormat.Csv => await CsvDataParser.PreviewAsync(filePath, delimiterHint, ct),
            ImportFileFormat.Json => await JsonDataParser.PreviewAsync(filePath, ct),
            ImportFileFormat.Excel => await ExcelDataParser.PreviewAsync(filePath, ct),
            _ => throw new NotSupportedException($"Formato de arquivo {format} não suportado.")
        };
    }

    public async Task<ImportExecutionResult> ExecuteImportAsync(
        IDatabaseService databaseService,
        ImportConfiguration config,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.FilePath) || !File.Exists(config.FilePath))
        {
            return ImportExecutionResult.Failure("Arquivo para importação não encontrado.");
        }

        if (string.IsNullOrWhiteSpace(config.TargetTable))
        {
            return ImportExecutionResult.Failure("Tabela de destino não especificada.");
        }

        // Active mapped columns
        var activeMappings = config.Mappings
            .Select((m, index) => new { Mapping = m, SourceIndex = index })
            .Where(x => !x.Mapping.IsIgnored && !string.IsNullOrWhiteSpace(x.Mapping.TargetColumn))
            .ToList();

        if (activeMappings.Count == 0)
        {
            return ImportExecutionResult.Failure("Nenhuma coluna mapeada para importação.");
        }

        var sw = Stopwatch.StartNew();
        long insertedCount = 0;
        long failedCount = 0;
        var errorMessages = new List<string>();

        // Build INSERT SQL Template
        var isPostgres = databaseService.Config.Type == DatabaseType.PostgreSQL;
        var targetTableEscaped = EscapeIdentifier(config.TargetTable, isPostgres);
        var targetColsEscaped = string.Join(", ", activeMappings.Select(m => EscapeIdentifier(m.Mapping.TargetColumn!, isPostgres)));
        var paramPlaceholders = string.Join(", ", activeMappings.Select((m, i) => $"@p{i}"));

        var insertSql = $"INSERT INTO {targetTableEscaped} ({targetColsEscaped}) VALUES ({paramPlaceholders});";

        var batchSize = Math.Max(1, config.BatchSize);
        var currentBatch = new List<ParameterizedCommand>(batchSize);
        int totalProcessed = 0;

        try
        {
            var rowSource = GetRowStreamAsync(config, ct);

            await foreach (var row in rowSource.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                totalProcessed++;

                var parameters = new Dictionary<string, object?>();
                for (int i = 0; i < activeMappings.Count; i++)
                {
                    var sourceIdx = activeMappings[i].SourceIndex;
                    var mapping = activeMappings[i].Mapping;
                    var rawVal = sourceIdx < row.Count ? row[sourceIdx] : null;

                    var converted = ConvertValue(rawVal, mapping.TargetDataType, mapping.DefaultValue);
                    parameters[$"@p{i}"] = converted;
                }

                currentBatch.Add(new ParameterizedCommand(insertSql, parameters));

                if (currentBatch.Count >= batchSize)
                {
                    var (ins, fail) = await ExecuteBatchAsync(databaseService, currentBatch, config.ErrorStrategy, 
                        totalProcessed - currentBatch.Count + 1, errorMessages, ct);
                    insertedCount += ins;
                    failedCount += fail;
                    currentBatch.Clear();
                    progress?.Report((int)insertedCount);
                }
            }

            if (currentBatch.Count > 0)
            {
                var (ins, fail) = await ExecuteBatchAsync(databaseService, currentBatch, config.ErrorStrategy, 
                    totalProcessed - currentBatch.Count + 1, errorMessages, ct);
                insertedCount += ins;
                failedCount += fail;
                currentBatch.Clear();
                progress?.Report((int)insertedCount);
            }

            sw.Stop();
            return new ImportExecutionResult
            {
                Success = config.ErrorStrategy != ImportErrorStrategy.RollbackOnError || failedCount == 0,
                InsertedRowsCount = insertedCount,
                FailedRowsCount = failedCount,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                Errors = errorMessages
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ImportExecutionResult.Failure("Operação de importação cancelada pelo usuário.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var res = ImportExecutionResult.Failure(ex.Message, sw.Elapsed.TotalMilliseconds);
            res.Errors.AddRange(errorMessages);
            if (res.Errors.Count == 0)
            {
                res.Errors.Add(ex.Message);
            }
            return res;
        }
    }

    private static async Task<(long inserted, long failed)> ExecuteBatchAsync(
        IDatabaseService databaseService,
        List<ParameterizedCommand> batch,
        ImportErrorStrategy strategy,
        int startRowNumber,
        List<string> errorMessages,
        CancellationToken ct)
    {
        long inserted = 0;
        long failed = 0;

        if (strategy == ImportErrorStrategy.RollbackOnError)
        {
            var result = await databaseService.ExecuteBatchTransactionalAsync(batch, ct);
            if (!result.Success)
            {
                var msg = $"Transação revertida no lote: {result.ErrorMessage}";
                errorMessages.Add(msg);
                throw new InvalidOperationException(msg);
            }
            inserted = batch.Count;
        }
        else
        {
            // Try batch first for performance
            var result = await databaseService.ExecuteBatchTransactionalAsync(batch, ct);
            if (result.Success)
            {
                inserted = batch.Count;
            }
            else
            {
                // Fall back to line-by-line to isolate errors
                for (int i = 0; i < batch.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var cmd = batch[i];
                    var lineNum = startRowNumber + i;

                    var singleResult = await databaseService.ExecuteBatchTransactionalAsync(new[] { cmd }, ct);
                    if (singleResult.Success)
                    {
                        inserted++;
                    }
                    else
                    {
                        failed++;
                        if (errorMessages.Count < 100)
                        {
                            errorMessages.Add($"Linha {lineNum}: {singleResult.ErrorMessage}");
                        }
                    }
                }
            }
        }

        return (inserted, failed);
    }

    private static async IAsyncEnumerable<IReadOnlyList<object?>> GetRowStreamAsync(
        ImportConfiguration config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (config.Format == ImportFileFormat.Csv)
        {
            var delimiter = config.Delimiter ?? ',';
            await foreach (var row in CsvDataParser.ReadAllRowsAsync(config.FilePath, delimiter, config.FirstRowIsHeader, ct))
            {
                yield return row;
            }
        }
        else if (config.Format == ImportFileFormat.Json)
        {
            var headers = config.Mappings.Select(m => m.SourceColumn).ToList();
            await foreach (var row in JsonDataParser.ReadAllRowsAsync(config.FilePath, headers, ct))
            {
                yield return row;
            }
        }
        else if (config.Format == ImportFileFormat.Excel)
        {
            await foreach (var row in ExcelDataParser.ReadAllRowsAsync(config.FilePath, ct))
            {
                yield return row;
            }
        }
    }

    public static object? ConvertValue(object? rawVal, string? targetDataType, object? defaultValue = null)
    {
        if (rawVal == null || rawVal == DBNull.Value)
        {
            return defaultValue ?? DBNull.Value;
        }

        var str = rawVal.ToString()?.Trim();
        if (string.IsNullOrEmpty(str))
        {
            return defaultValue ?? DBNull.Value;
        }

        var dt = targetDataType?.ToUpperInvariant() ?? "TEXT";

        if (dt.Contains("INT"))
        {
            if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (long.TryParse(str, out var l2)) return l2;
            return defaultValue ?? DBNull.Value;
        }

        if (dt.Contains("NUM") || dt.Contains("DEC") || dt.Contains("REAL") || dt.Contains("FLOAT") || dt.Contains("DOUBLE"))
        {
            var normalized = str.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return defaultValue ?? DBNull.Value;
        }

        if (dt.Contains("BOOL"))
        {
            if (bool.TryParse(str, out var b)) return b;
            if (str == "1" || str.Equals("sim", StringComparison.OrdinalIgnoreCase) || str.Equals("y", StringComparison.OrdinalIgnoreCase)) return true;
            if (str == "0" || str.Equals("nao", StringComparison.OrdinalIgnoreCase) || str.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue ?? DBNull.Value;
        }

        if (dt.Contains("DATE") || dt.Contains("TIME"))
        {
            if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToString("yyyy-MM-dd HH:mm:ss.fff");
            if (DateTime.TryParse(str, out var dateLocal)) return dateLocal.ToString("yyyy-MM-dd HH:mm:ss.fff");
            return str;
        }

        return str;
    }

    private static string EscapeIdentifier(string identifier, bool isPostgres)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return "\"\"";
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static ImportFileFormat DetectFormatFromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => ImportFileFormat.Json,
            ".xlsx" or ".xls" => ImportFileFormat.Excel,
            _ => ImportFileFormat.Csv
        };
    }
}
