using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Relatório consolidado da execução de uma importação de dados.
/// </summary>
public class ImportExecutionResult
{
    public bool Success { get; set; } = true;
    public long InsertedRowsCount { get; set; }
    public long FailedRowsCount { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public string ElapsedFormatted => $"{ElapsedMilliseconds:F1} ms";
    public string Summary => $"{InsertedRowsCount:N0} inseridas, {FailedRowsCount:N0} com erro ({ElapsedFormatted})";

    public static ImportExecutionResult Failure(string message, double elapsedMs = 0) => new()
    {
        Success = false,
        ErrorMessage = message,
        ElapsedMilliseconds = elapsedMs
    };
}
