namespace Runix.Core.Models;

/// <summary>
/// Resultado detalhado de uma operação de exportação de dados.
/// </summary>
public class ExportResult
{
    public bool Success { get; set; } = true;
    public long RowsExported { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }

    public string FileSizeFormatted => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB"
    };

    public string ElapsedFormatted => $"{ElapsedMilliseconds:F1} ms";

    public static ExportResult Failure(string errorMessage, double elapsedMs = 0) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ElapsedMilliseconds = elapsedMs
    };
}
